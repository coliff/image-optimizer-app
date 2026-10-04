namespace ImageOptimizer.Core;

/// <summary>An optimized copy of the input, written somewhere in the work directory.</summary>
/// <param name="Path">Location of the candidate file.</param>
/// <param name="PixelDataUntouched">
/// True when the tool only rewrote the container (e.g. removed metadata) and never re-encoded the image,
/// so there is nothing to verify pixel by pixel.
/// </param>
public sealed record Candidate(string Path, bool PixelDataUntouched = false);

public sealed record OptimizationContext(string InputPath, string WorkDirectory, OptimizerSettings Settings)
{
  public string WorkFile(string name) => System.IO.Path.Combine(WorkDirectory, name);
}

public sealed record CandidateSet(IReadOnlyList<Candidate> Candidates, string? SkipReason = null)
{
  public static CandidateSet Skip(string reason) => new([], reason);
}

public interface IFormatOptimizer
{
  ImageFormat Format { get; }
  Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken);
}

/// <summary>PNG: oxipng (lossless filter, bit depth, palette and deflate optimization).</summary>
public sealed class PngOptimizer(IToolRunner tools) : IFormatOptimizer
{
  // Chunks that change how an image looks; everything else is metadata.
  private const string DisplayChunks = "cICP,iCCP,sRGB,gAMA,cHRM,mDCV,cLLI,pHYs";

  public ImageFormat Format => ImageFormat.Png;

  public async Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
  {
    if (ImageInspector.IsAnimatedPng(await ReadHeadAsync(context.InputPath, cancellationToken)))
      return CandidateSet.Skip("Animated PNGs are left untouched");

    var output = context.WorkFile("oxipng.png");
    var args = new List<string> { "--quiet", "--interlace", "keep" };
    if (context.Settings.MaximumCompression)
      args.AddRange(["--opt", "max", "--zopfli", "--fast"]);
    else
      args.AddRange(["--opt", "4"]);
    if (context.Settings.StripMetadata)
      args.AddRange(["--keep", DisplayChunks]);
    args.AddRange(["--out", output, context.InputPath]);

    await tools.RunAsync("oxipng", args, cancellationToken).ConfigureAwait(false);
    return new CandidateSet([new Candidate(output)]);
  }

  private static async Task<byte[]> ReadHeadAsync(string path, CancellationToken cancellationToken)
  {
    // acTL has to appear before IDAT, which is almost always within the first few KB.
    await using var stream = File.OpenRead(path);
    var buffer = new byte[Math.Min(stream.Length, 1 << 20)];
    var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
    return buffer[..read];
  }
}

/// <summary>JPEG: jpegtran (lossless Huffman optimization and progressive re-ordering of the DCT data).</summary>
public sealed class JpegOptimizer(IToolRunner tools) : IFormatOptimizer
{
  public ImageFormat Format => ImageFormat.Jpeg;

  public async Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
  {
    var copy = await ChooseCopyModeAsync(context, cancellationToken).ConfigureAwait(false);

    var baseline = context.WorkFile("baseline.jpg");
    var tasks = new List<Task>
        {
            tools.RunAsync("jpegtran", ["-copy", copy, "-optimize", "-outfile", baseline, context.InputPath], cancellationToken),
        };
    var candidates = new List<Candidate> { new(baseline) };

    if (context.Settings.AllowProgressiveJpeg)
    {
      var progressive = context.WorkFile("progressive.jpg");
      tasks.Add(tools.RunAsync("jpegtran", ["-copy", copy, "-progressive", "-optimize", "-outfile", progressive, context.InputPath], cancellationToken));
      candidates.Add(new(progressive));
    }

    await Task.WhenAll(tasks).ConfigureAwait(false);
    return new CandidateSet(candidates);
  }

  private static async Task<string> ChooseCopyModeAsync(OptimizationContext context, CancellationToken cancellationToken)
  {
    if (!context.Settings.StripMetadata)
      return "all";

    // Dropping EXIF would also drop the orientation tag and turn photos sideways, so keep it when it matters.
    var bytes = await File.ReadAllBytesAsync(context.InputPath, cancellationToken).ConfigureAwait(false);
    var orientation = ImageInspector.GetJpegExifOrientation(bytes);
    return orientation is > 1 ? "all" : "icc";
  }
}

/// <summary>
/// WebP: lossless images are re-encoded losslessly with cwebp; lossy and animated images only have
/// metadata removed with webpmux, because re-encoding them would lose quality.
/// </summary>
public sealed class WebPOptimizer(IToolRunner tools) : IFormatOptimizer
{
  public ImageFormat Format => ImageFormat.WebP;

  public async Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
  {
    var info = ImageInspector.InspectWebP(await File.ReadAllBytesAsync(context.InputPath, cancellationToken).ConfigureAwait(false));
    if (!info.IsValid)
      return CandidateSet.Skip("Not a valid WebP file");

    if (info.IsStillLossless)
      return new CandidateSet([await ReencodeLosslessAsync(context, info, cancellationToken).ConfigureAwait(false)]);

    if (context.Settings.StripMetadata && (info.HasExif || info.HasXmp))
      return new CandidateSet([await StripMetadataAsync(context, info, cancellationToken).ConfigureAwait(false)]);

    return CandidateSet.Skip(info.IsAnimated
        ? "Animated WebP has nothing to remove losslessly"
        : "Lossy WebP can't be recompressed without losing quality");
  }

  private async Task<Candidate> ReencodeLosslessAsync(OptimizationContext context, WebPInfo info, CancellationToken cancellationToken)
  {
    var encoded = context.WorkFile("cwebp.webp");
    var effort = context.Settings.MaximumCompression ? "9" : "6";
    await tools.RunAsync("cwebp",
        ["-quiet", "-lossless", "-exact", "-z", effort, "-metadata", "none", context.InputPath, "-o", encoded],
        cancellationToken).ConfigureAwait(false);

    // cwebp doesn't carry metadata over from WebP input, so put back what we want to keep.
    var current = encoded;
    var keep = new List<string>();
    if (info.HasIccProfile)
      keep.Add("icc");
    if (!context.Settings.StripMetadata)
    {
      if (info.HasExif)
        keep.Add("exif");
      if (info.HasXmp)
        keep.Add("xmp");
    }

    foreach (var kind in keep)
    {
      var chunk = context.WorkFile($"chunk.{kind}");
      var next = context.WorkFile($"cwebp-{kind}.webp");
      await tools.RunAsync("webpmux", ["-get", kind, context.InputPath, "-o", chunk], cancellationToken).ConfigureAwait(false);
      await tools.RunAsync("webpmux", ["-set", kind, chunk, current, "-o", next], cancellationToken).ConfigureAwait(false);
      current = next;
    }
    return new Candidate(current);
  }

  private async Task<Candidate> StripMetadataAsync(OptimizationContext context, WebPInfo info, CancellationToken cancellationToken)
  {
    var current = context.InputPath;
    foreach (var (kind, present) in new[] { ("exif", info.HasExif), ("xmp", info.HasXmp) })
    {
      if (!present)
        continue;
      var next = context.WorkFile($"strip-{kind}.webp");
      await tools.RunAsync("webpmux", ["-strip", kind, current, "-o", next], cancellationToken).ConfigureAwait(false);
      current = next;
    }
    return new Candidate(current, PixelDataUntouched: true);
  }
}

/// <summary>GIF: gifsicle -O3 (lossless frame and LZW optimization).</summary>
public sealed class GifOptimizer(IToolRunner tools) : IFormatOptimizer
{
  public ImageFormat Format => ImageFormat.Gif;

  public async Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
  {
    var output = context.WorkFile("gifsicle.gif");
    var args = new List<string> { "--no-warnings", "-O3" };
    if (context.Settings.StripMetadata)
      args.AddRange(["--no-comments", "--no-names"]);
    args.AddRange([context.InputPath, "-o", output]);

    await tools.RunAsync("gifsicle", args, cancellationToken).ConfigureAwait(false);
    return new CandidateSet([new Candidate(output)]);
  }
}
