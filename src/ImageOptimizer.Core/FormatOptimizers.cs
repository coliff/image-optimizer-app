using System.Globalization;

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
    var data = await File.ReadAllBytesAsync(context.InputPath, cancellationToken).ConfigureAwait(false);
    if (ImageInspector.IsAnimatedPng(data))
      return CandidateSet.Skip(Strings.AnimatedLeftUntouched(ImageFormat.Png));

    var output = context.WorkFile("oxipng.png");
    var args = new List<string> { "--quiet", "--interlace", "keep" };
    if (context.Settings.MaximumCompression)
      args.AddRange(["--opt", "max", "--zopfli", "--fast"]);
    else
      args.AddRange(["--opt", "4"]);
    if (context.Settings.StripMetadata)
    {
      // Like JPEGs, keep the EXIF data when it turns the image, so it isn't shown sideways.
      var keep = ImageInspector.GetPngExifOrientation(data) is > 1 ? DisplayChunks + ",eXIf" : DisplayChunks;
      args.AddRange(["--keep", keep]);
    }
    args.AddRange(["--out", output, context.InputPath]);

    await tools.RunAsync("oxipng", args, cancellationToken).ConfigureAwait(false);
    return new CandidateSet([new Candidate(output)]);
  }
}

/// <summary>JPEG: jpegtran (lossless Huffman optimization and progressive re-ordering of the DCT data).</summary>
public sealed class JpegOptimizer(IToolRunner tools) : IFormatOptimizer
{
  public ImageFormat Format => ImageFormat.Jpeg;

  public async Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
  {
    var data = await File.ReadAllBytesAsync(context.InputPath, cancellationToken).ConfigureAwait(false);
    if (ImageInspector.HasJpegTrailingData(data))
      return CandidateSet.Skip(Strings.JpegTrailingData);
    var copy = ChooseCopyMode(context.Settings, data);

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

  private static string ChooseCopyMode(OptimizerSettings settings, byte[] data)
  {
    if (!settings.StripMetadata)
      return "all";

    // Dropping EXIF would also drop the orientation tag and turn photos sideways, so keep it when it matters.
    var orientation = ImageInspector.GetJpegExifOrientation(data);
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
      return CandidateSet.Skip(Strings.InvalidFile(ImageFormat.WebP));

    if (info.IsStillLossless)
      return new CandidateSet([await ReencodeLosslessAsync(context, info, cancellationToken).ConfigureAwait(false)]);

    if (context.Settings.StripMetadata && ((info.HasExif && !info.ExifRotates) || info.HasXmp))
      return new CandidateSet([await StripMetadataAsync(context, info, cancellationToken).ConfigureAwait(false)]);

    return CandidateSet.Skip(info.IsAnimated
        ? Strings.AnimatedWebPNothingToRemove
        : Strings.LossyCannotRecompress(ImageFormat.WebP));
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
    if (info.HasExif && (!context.Settings.StripMetadata || info.ExifRotates))
      keep.Add("exif");
    if (info.HasXmp && !context.Settings.StripMetadata)
      keep.Add("xmp");

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
    // Like JPEGs, keep the EXIF data when it turns the image, so it isn't shown sideways.
    foreach (var (kind, present) in new[] { ("exif", info.HasExif && !info.ExifRotates), ("xmp", info.HasXmp) })
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

/// <summary>
/// AVIF: lossless AVIFs (full-resolution RGB planes) are decoded with avifdec and re-encoded losslessly with
/// avifenc at a higher effort. Lossy AVIFs are left alone, because re-encoding them would lose quality and
/// there is no tool that removes their metadata without re-encoding.
/// </summary>
public sealed class AvifOptimizer(IToolRunner tools) : IFormatOptimizer
{
  public ImageFormat Format => ImageFormat.Avif;

  public async Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
  {
    var info = AvifInspector.Inspect(await File.ReadAllBytesAsync(context.InputPath, cancellationToken).ConfigureAwait(false));
    if (!info.IsValid)
      return CandidateSet.Skip(Strings.InvalidFile(ImageFormat.Avif));
    if (info.IsAnimated)
      return CandidateSet.Skip(Strings.AnimatedLeftUntouched(ImageFormat.Avif));
    if (info.UnsupportedReason is not null)
      return CandidateSet.Skip(info.UnsupportedReason);
    if (info.AlphaPremultiplied)
      return CandidateSet.Skip(Strings.AvifPremultipliedAlpha);
    if (!info.IsLikelyLossless)
      return CandidateSet.Skip(Strings.LossyCannotRecompress(ImageFormat.Avif));

    // y4m keeps the decoded planes exactly as stored (depth, range and alpha included), with no RGB conversion.
    var planes = context.WorkFile("planes.y4m");
    await tools.RunAsync("avifdec", ["--jobs", "all", context.InputPath, planes], cancellationToken).ConfigureAwait(false);

    var args = new List<string> { "--lossless", "--jobs", "all", "--cicp", $"{info.Cicp!.Value.Primaries}/{info.Cicp.Value.Transfer}/{info.Cicp.Value.Matrix}" };
    if (info.Rotation is { } rotation)
      args.AddRange(["--irot", rotation.ToString(CultureInfo.InvariantCulture)]);
    if (info.MirrorAxis is { } axis)
      args.AddRange(["--imir", axis.ToString(CultureInfo.InvariantCulture)]);
    if (info.CleanAperture is { } clap)
      // The offset numerators (fields 4 and 6) are signed.
      args.AddRange(["--clap", string.Join(',', clap.Select((v, i) => (i is 4 or 6 ? (long)(int)v : v).ToString(CultureInfo.InvariantCulture)))]);
    if (info.PixelAspectRatio is { } pasp)
      args.AddRange(["--pasp", FormattableString.Invariant($"{pasp.Horizontal},{pasp.Vertical}")]);
    if (info.ContentLightLevel is { } clli)
      args.AddRange(["--clli", FormattableString.Invariant($"{clli.MaxCll},{clli.MaxPall}")]);

    var keepExif = info.ExifTiff is not null && !context.Settings.StripMetadata;
    var keepXmp = info.Xmp is not null && !context.Settings.StripMetadata;
    if (info.IccProfile is { } icc)
      args.AddRange(["--icc", await WriteAsync(context.WorkFile("profile.icc"), icc, cancellationToken).ConfigureAwait(false)]);
    if (keepExif)
      args.AddRange(["--exif", await WriteAsync(context.WorkFile("metadata.exif"), info.ExifTiff!, cancellationToken).ConfigureAwait(false)]);
    if (keepXmp)
      args.AddRange(["--xmp", await WriteAsync(context.WorkFile("metadata.xmp"), info.Xmp!, cancellationToken).ConfigureAwait(false)]);

    // Lossless AV1 doesn't always get smaller at slower speeds, so try a few and keep the smallest.
    int[] speeds = context.Settings.MaximumCompression ? [0, 2, 4] : [4];
    var outputs = speeds.Select(speed => context.WorkFile($"avifenc-s{speed}.avif")).ToList();
    await Task.WhenAll(speeds.Select((speed, i) =>
        tools.RunAsync("avifenc", [.. args, "--speed", speed.ToString(CultureInfo.InvariantCulture), planes, outputs[i]], cancellationToken))).ConfigureAwait(false);

    // Only keep encodes that carry over everything that affects how the image looks, and the metadata we keep.
    var candidates = new List<Candidate>();
    foreach (var output in outputs)
    {
      var encoded = AvifInspector.Inspect(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
      if (encoded.SameAppearance(info) &&
          AvifInfo.ArraysEqual(encoded.ExifTiff, keepExif ? info.ExifTiff : null) &&
          AvifInfo.ArraysEqual(encoded.Xmp, keepXmp ? info.Xmp : null))
        candidates.Add(new Candidate(output));
    }
    return new CandidateSet(candidates);
  }

  private static async Task<string> WriteAsync(string path, byte[] contents, CancellationToken cancellationToken)
  {
    await File.WriteAllBytesAsync(path, contents, cancellationToken).ConfigureAwait(false);
    return path;
  }
}

/// <summary>
/// JPEG XL: lossless images are re-encoded losslessly with cjxl at a higher effort, and recompressed JPEGs are
/// rebuilt with djxl and recompressed again, so the original JPEG can still be restored bit for bit. Lossy
/// images are left alone, because re-encoding them would lose quality.
/// </summary>
public sealed class JxlOptimizer(IToolRunner tools) : IFormatOptimizer
{
  public ImageFormat Format => ImageFormat.JpegXl;

  public async Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
  {
    var info = JxlInspector.Inspect(await File.ReadAllBytesAsync(context.InputPath, cancellationToken).ConfigureAwait(false));
    if (!info.IsValid)
      return CandidateSet.Skip(Strings.InvalidFile(ImageFormat.JpegXl));
    if (info.IsAnimated)
      return CandidateSet.Skip(Strings.AnimatedLeftUntouched(ImageFormat.JpegXl));
    if (info.UnsupportedReason is not null)
      return CandidateSet.Skip(info.UnsupportedReason);
    if (info.HasJpegReconstruction)
      return await RecompressJpegAsync(context, info, cancellationToken).ConfigureAwait(false);
    if (info.XybEncoded)
      return CandidateSet.Skip(Strings.LossyCannotRecompress(ImageFormat.JpegXl));
    // cjxl applies the orientation to the pixels when it reads a JPEG XL file, so it can't be kept as a flag.
    if (info.Orientation != 1)
      return CandidateSet.Skip(Strings.JxlRotated);
    if (info.ExponentBitsPerSample != 0 || info.BitsPerSample > 16)
      return CandidateSet.Skip(Strings.JxlHighBitDepth);
    if (info.ExtraChannels.Count > 1 || info.ExtraChannels.Any(c => c.Type != 0))
      return CandidateSet.Skip(Strings.JxlExtraChannels);

    var args = new List<string> { "--distance", "0", "--quiet" };
    if (context.Settings.StripMetadata)
      args.AddRange(["-x", "strip=exif", "-x", "strip=xmp", "-x", "strip=jumbf"]);
    var outputs = await EncodeAsync(context, context.InputPath, args, cancellationToken).ConfigureAwait(false);

    // Only keep encodes that carry over everything that affects how the image looks, and the metadata we keep.
    var keepMetadata = !context.Settings.StripMetadata;
    var candidates = new List<Candidate>();
    foreach (var output in outputs)
    {
      var encoded = JxlInspector.Inspect(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
      if (encoded.SameAppearance(info) && !encoded.HasJpegReconstruction &&
          AvifInfo.ArraysEqual(encoded.ExifTiff, keepMetadata ? info.ExifTiff : null) &&
          AvifInfo.ArraysEqual(encoded.Xmp, keepMetadata ? info.Xmp : null) &&
          AvifInfo.ArraysEqual(encoded.Jumbf, keepMetadata ? info.Jumbf : null))
        candidates.Add(new Candidate(output));
    }
    return new CandidateSet(candidates);
  }

  /// <summary>
  /// Rebuilds the original JPEG and recompresses it. The JPEG carries its own metadata, which has to stay
  /// for the reconstruction to be exact, so nothing is removed.
  /// </summary>
  private async Task<CandidateSet> RecompressJpegAsync(OptimizationContext context, JxlInfo info, CancellationToken cancellationToken)
  {
    var jpeg = context.WorkFile("reconstructed.jpg");
    await tools.RunAsync("djxl", [context.InputPath, jpeg, "--reconstruct_jpeg", "--quiet"], cancellationToken).ConfigureAwait(false);

    var outputs = await EncodeAsync(context, jpeg, ["--lossless_jpeg", "1", "--brotli_effort", "11", "--quiet"], cancellationToken).ConfigureAwait(false);

    var candidates = new List<Candidate>();
    foreach (var output in outputs)
    {
      var encoded = JxlInspector.Inspect(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
      if (encoded.SameAppearance(info) && encoded.HasJpegReconstruction)
        candidates.Add(new Candidate(output));
    }
    return new CandidateSet(candidates);
  }

  private async Task<List<string>> EncodeAsync(OptimizationContext context, string input, List<string> args, CancellationToken cancellationToken)
  {
    // A higher effort isn't always smaller, so with maximum compression try both and keep the smallest.
    // Encoding large images in one piece (no buffering) is slower but finds a few more percent.
    int[] efforts = context.Settings.MaximumCompression ? [9, 10] : [9];
    if (context.Settings.MaximumCompression)
      args = [.. args, "--buffering", "0"];
    var outputs = efforts.Select(effort => context.WorkFile($"cjxl-e{effort}.jxl")).ToList();
    await Task.WhenAll(efforts.Select((effort, i) =>
        tools.RunAsync("cjxl", [input, outputs[i], .. args, "--effort", effort.ToString(CultureInfo.InvariantCulture)], cancellationToken))).ConfigureAwait(false);
    return outputs;
  }
}
