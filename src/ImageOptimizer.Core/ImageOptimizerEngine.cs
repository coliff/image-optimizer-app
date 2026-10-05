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
            new SvgOptimizer(() => (tools as ToolRunner)?.LocateFile(SvgOptimizer.ScriptName)),
          ],
          new CompositeVerifier([.. extraVerifiers, new WebPPixelVerifier(tools), new AvifPixelVerifier(tools), new JxlPixelVerifier(tools)]));

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
    try
    {
      Directory.CreateDirectory(workDirectory);

      // Tools only ever read a private copy, so a misbehaving tool can't damage the original.
      var input = Path.Combine(workDirectory, "input" + ExtensionFor(format));
      File.Copy(path, input);

      var set = await optimizer.CreateCandidatesAsync(new OptimizationContext(input, workDirectory, settings), cancellationToken).ConfigureAwait(false);
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
      File.Copy(winner.Path, staging);
      File.Replace(staging, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
      staging = null;

      if (settings.PreserveModifiedDate)
        File.SetLastWriteTimeUtc(path, originalWriteTime);

      return Result(OptimizationStatus.Optimized, finalSize: new FileInfo(path).Length);
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
    _ => ".bin",
  };
}
