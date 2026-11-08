namespace ImageOptimizer.Core;

public enum OptimizationStatus
{
  /// <summary>A smaller, pixel-identical file replaced the original.</summary>
  Optimized,
  /// <summary>Nothing smaller was found; the original is untouched.</summary>
  AlreadyOptimized,
  /// <summary>The file was deliberately left alone (unsupported, disabled in settings, animated, ...).</summary>
  Skipped,
  /// <summary>Something went wrong; the original is untouched.</summary>
  Failed,
}

public sealed record OptimizationResult(
    OptimizationStatus Status,
    ImageFormat Format,
    long OriginalSize,
    long FinalSize,
    string? Message = null)
{
  public long BytesSaved => OriginalSize - FinalSize;
  public double SavingsRatio => OriginalSize > 0 ? (double)BytesSaved / OriginalSize : 0;
}

/// <summary>
/// Optimizes one file at a time. The original is only ever touched in a single final step, and only when
/// a verified candidate is strictly smaller; in every other case the file on disk is left exactly as it was.
/// </summary>
public sealed class ImageOptimizerEngine
{
  private readonly Dictionary<ImageFormat, IFormatOptimizer> _optimizers;
  private readonly IPixelVerifier _verifier;
  private readonly string _workRoot;

  // Maximum compression runs several encoders with every core each for AVIF and JPEG XL (and JPEG XL encodes large
  // images in one piece), so a few large photos at once could use most of the PC's memory. Those take turns.
  private readonly SemaphoreSlim _heavyGate = new(1, 1);

  public ImageOptimizerEngine(IEnumerable<IFormatOptimizer> optimizers, IPixelVerifier verifier, string? workRoot = null)
  {
    _optimizers = optimizers.ToDictionary(o => o.Format);
    _verifier = verifier;
    _workRoot = workRoot ?? Path.Combine(Path.GetTempPath(), "ImageOptimizer");
  }

  public static ImageOptimizerEngine CreateDefault(IToolRunner tools, params IPixelVerifier[] extraVerifiers) =>
      new(
          [
            new PngOptimizer(tools),
            new JpegOptimizer(tools),
            new WebPOptimizer(tools),
            new AvifOptimizer(tools),
            new JxlOptimizer(tools),
            new GifOptimizer(tools),
            new IcoOptimizer(tools),
            new SvgOptimizer(() => (tools as ToolRunner)?.LocateFile(SvgOptimizer.ScriptName)),
          ],
          new CompositeVerifier([.. extraVerifiers, new WebPPixelVerifier(tools), new AvifPixelVerifier(tools), new JxlPixelVerifier(tools), new GifPixelVerifier()]));

  public async Task<OptimizationResult> OptimizeAsync(string path, OptimizerSettings settings, CancellationToken cancellationToken = default)
  {
    FileInfo original;
    ImageFormat format;
    try
    {
      original = new FileInfo(path);
      if (!original.Exists)
        return new(OptimizationStatus.Failed, ImageFormat.Unknown, 0, 0, Strings.FileNotFound);
      format = ImageFormats.Detect(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return new(OptimizationStatus.Failed, ImageFormat.Unknown, 0, 0, ex.Message);
    }

    var originalSize = original.Length;
    var originalWriteTime = original.LastWriteTimeUtc;
    OptimizationResult Result(OptimizationStatus status, string? message = null, long? finalSize = null) =>
        new(status, format, originalSize, finalSize ?? originalSize, message);

    if (format == ImageFormat.Unknown || !_optimizers.TryGetValue(format, out var optimizer))
      return Result(OptimizationStatus.Skipped, Strings.NotAnImage);
    if (!settings.IsEnabled(format))
      return Result(OptimizationStatus.Skipped, Strings.FormatTurnedOff(format));
    if (originalSize == 0)
      return Result(OptimizationStatus.Skipped, Strings.FileEmpty);

    var workDirectory = Path.Combine(_workRoot, Guid.NewGuid().ToString("N"));
    string? staging = null;
    string? pendingRecord = null;
    try
    {
      Directory.CreateDirectory(workDirectory);

      // Tools only ever read a private copy, so a misbehaving tool can't damage the original.
      var input = Path.Combine(workDirectory, "input" + ExtensionFor(format));
      File.Copy(path, input);

      CandidateSet set;
      var heavy = IsHeavy(format, settings);
      if (heavy)
        await _heavyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        set = await optimizer.CreateCandidatesAsync(new OptimizationContext(input, workDirectory, settings), cancellationToken).ConfigureAwait(false);
      }
      finally
      {
        if (heavy)
          _heavyGate.Release();
      }
      if (set.SkipReason is not null)
        return Result(OptimizationStatus.Skipped, set.SkipReason);

      var ordered = set.Candidates
          .Select(c => (Candidate: c, Size: SizeOf(c.Path)))
          .Where(c => c.Size > 0 && c.Size < originalSize)
          .OrderBy(c => c.Size)
          .ToList();

      Candidate? winner = null;
      var rejected = false;
      foreach (var (candidate, _) in ordered)
      {
        if (!candidate.PixelDataUntouched &&
            await _verifier.VerifyAsync(format, input, candidate.Path, workDirectory, cancellationToken).ConfigureAwait(false) == VerificationResult.Different)
        {
          rejected = true;
          continue;
        }
        winner = candidate;
        break;
      }

      if (winner is null)
      {
        return rejected
            ? Result(OptimizationStatus.Failed, Strings.PixelsDiffer)
            : Result(OptimizationStatus.AlreadyOptimized);
      }

      cancellationToken.ThrowIfCancellationRequested();

      // Don't clobber edits someone made to the file while we were working on it.
      original.Refresh();
      if (!original.Exists || original.Length != originalSize || original.LastWriteTimeUtc != originalWriteTime)
        return Result(OptimizationStatus.Failed, Strings.FileChanged);

      // Stage next to the original so the final swap is a same-volume replace, not a copy.
      staging = StagingPath(original);
      // Noted down first, so a staging copy left by a crash or a kill from Task Manager can be found and removed.
      pendingRecord = RecordPending(staging);
      File.Copy(winner.Path, staging);
      File.Replace(staging, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
      staging = null;

      // The smaller file is in place now, so nothing after this may report the file as failed. Antivirus,
      // OneDrive or Explorer's thumbnailer often open a file the moment it changes, which can make these throw.
      if (settings.PreserveModifiedDate)
        TryRestoreModifiedDate(path, originalWriteTime);
      return Result(OptimizationStatus.Optimized, finalSize: TrySizeOf(path) ?? SizeOf(winner.Path));
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (ToolNotFoundException ex)
    {
      return Result(OptimizationStatus.Failed, ex.Message);
    }
    catch (ToolFailedException ex)
    {
      return Result(OptimizationStatus.Failed, ex.Message);
    }
    catch (UnauthorizedAccessException)
    {
      return Result(OptimizationStatus.Failed, Strings.CannotWrite);
    }
    catch (IOException ex)
    {
      return Result(OptimizationStatus.Failed, ex.Message);
    }
    finally
    {
      TryDelete(staging);
      // Keep the note while a staging copy that couldn't be deleted is still there, so a later start removes it.
      if (staging is null || !File.Exists(staging))
        TryDelete(pendingRecord);
      try
      {
        if (Directory.Exists(workDirectory))
          Directory.Delete(workDirectory, recursive: true);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
      }
    }
  }

  private const string StagingMarker = ".imageoptimizer-";
  private const string PendingFolder = "pending";

  /// <summary>True when a file takes a lot of memory and every core, so only one of them runs at a time.</summary>
  internal static bool IsHeavy(ImageFormat format, OptimizerSettings settings) =>
      settings.MaximumCompression && format is ImageFormat.Avif or ImageFormat.JpegXl;

  private string? RecordPending(string staging)
  {
    try
    {
      var folder = Path.Combine(_workRoot, PendingFolder);
      Directory.CreateDirectory(folder);
      var record = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".txt");
      File.WriteAllText(record, staging);
      return record;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      // Only the clean-up after a crash needs it; the file can still be optimized without it.
      return null;
    }
  }

  /// <summary>
  /// Removes what a crash or a kill from Task Manager left behind: work folders in the temporary folder older than
  /// <paramref name="minimumAge"/>, and staging copies next to images more than an hour old (a swap takes seconds).
  /// Newer ones are kept, because another copy of the app (in another sign-in session) may be using them right now.
  /// </summary>
  public void CleanUpLeftovers(TimeSpan minimumAge)
  {
    var now = DateTime.UtcNow;
    var cutoff = now - minimumAge;
    var stagingCutoff = now - TimeSpan.FromTicks(Math.Min(minimumAge.Ticks, TimeSpan.FromHours(1).Ticks));
    try
    {
      if (!Directory.Exists(_workRoot))
        return;

      var pending = Path.Combine(_workRoot, PendingFolder);
      if (Directory.Exists(pending))
      {
        foreach (var record in Directory.EnumerateFiles(pending, "*.txt"))
        {
          if (File.GetLastWriteTimeUtc(record) > stagingCutoff)
            continue;
          var staging = ReadRecord(record);
          // Only ever a staging copy, never an image of the user's, whatever the note says.
          var valid = staging is not null && IsStagingFile(staging) && Path.IsPathFullyQualified(staging);
          if (valid)
            TryDelete(staging);
          if (!valid || !File.Exists(staging))
            TryDelete(record);
        }
      }

      foreach (var folder in Directory.EnumerateDirectories(_workRoot))
      {
        if (Path.GetFileName(folder) == PendingFolder || Directory.GetLastWriteTimeUtc(folder) > cutoff)
          continue;
        try
        {
          Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
    }
  }

  private static string? ReadRecord(string record)
  {
    try
    {
      var text = File.ReadAllText(record).Trim();
      return text.Length > 0 ? text : null;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return null;
    }
  }

  /// <summary>
  /// A name like <c>photo.imageoptimizer-1a2b3c4d.jpg</c>, keeping the image's own extension. Antivirus ransomware
  /// checks flag programs that write unreadable files with a new extension tacked onto the original's name
  /// (<c>photo.jpg.&lt;id&gt;.tmp</c>) and then remove the original, which is what the old staging name looked like.
  /// </summary>
  internal static string StagingPath(FileInfo original) =>
      Path.Combine(
          original.DirectoryName!,
          $"{Path.GetFileNameWithoutExtension(original.Name)}{StagingMarker}{Guid.NewGuid().ToString("N")[..8]}{original.Extension}");

  /// <summary>True for a staging copy left behind if the app was closed mid-swap, so it isn't optimized as an image.</summary>
  public static bool IsStagingFile(string path) =>
      Path.GetFileNameWithoutExtension(path).Contains(StagingMarker, StringComparison.OrdinalIgnoreCase);

  private static long SizeOf(string path)
  {
    var info = new FileInfo(path);
    return info.Exists ? info.Length : 0;
  }

  /// <summary>Puts back the original "date modified", or leaves the new one if the file can't be changed right now.</summary>
  internal static bool TryRestoreModifiedDate(string path, DateTime lastWriteTimeUtc)
  {
    try
    {
      File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
      return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return false;
    }
  }

  private static long? TrySizeOf(string path)
  {
    try
    {
      return new FileInfo(path).Length;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return null;
    }
  }

  private static void TryDelete(string? path)
  {
    if (path is null)
      return;
    try
    {
      File.Delete(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
    }
  }

  private static string ExtensionFor(ImageFormat format) => format switch
  {
    ImageFormat.Png => ".png",
    ImageFormat.Jpeg => ".jpg",
    ImageFormat.WebP => ".webp",
    ImageFormat.Avif => ".avif",
    ImageFormat.Gif => ".gif",
    ImageFormat.Svg => ".svg",
    ImageFormat.JpegXl => ".jxl",
    ImageFormat.Ico => ".ico",
    _ => ".bin",
  };
}
