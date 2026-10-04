namespace ImageOptimizer.Core;

public enum VerificationResult
{
  /// <summary>Both files decode to exactly the same pixels.</summary>
  Identical,
  /// <summary>The pixels differ, so the candidate must be thrown away.</summary>
  Different,
  /// <summary>This verifier can't check this format.</summary>
  NotSupported,
}

/// <summary>Decodes the original and an optimized candidate and checks that no pixel changed.</summary>
public interface IPixelVerifier
{
  Task<VerificationResult> VerifyAsync(ImageFormat format, string originalPath, string candidatePath, string workDirectory, CancellationToken cancellationToken);
}

/// <summary>Tries each verifier in turn until one of them supports the format.</summary>
public sealed class CompositeVerifier(params IPixelVerifier[] verifiers) : IPixelVerifier
{
  public async Task<VerificationResult> VerifyAsync(ImageFormat format, string originalPath, string candidatePath, string workDirectory, CancellationToken cancellationToken)
  {
    foreach (var verifier in verifiers)
    {
      var result = await verifier.VerifyAsync(format, originalPath, candidatePath, workDirectory, cancellationToken).ConfigureAwait(false);
      if (result != VerificationResult.NotSupported)
        return result;
    }
    return VerificationResult.NotSupported;
  }
}

/// <summary>Decodes WebP files to raw RGBA with dwebp and compares them byte for byte.</summary>
public sealed class WebPPixelVerifier(IToolRunner tools) : IPixelVerifier
{
  public async Task<VerificationResult> VerifyAsync(ImageFormat format, string originalPath, string candidatePath, string workDirectory, CancellationToken cancellationToken)
  {
    if (format != ImageFormat.WebP)
      return VerificationResult.NotSupported;

    var id = Guid.NewGuid().ToString("N");
    var originalPixels = Path.Combine(workDirectory, $"verify-{id}-a.pam");
    var candidatePixels = Path.Combine(workDirectory, $"verify-{id}-b.pam");
    await tools.RunAsync("dwebp", ["-quiet", "-pam", originalPath, "-o", originalPixels], cancellationToken).ConfigureAwait(false);
    await tools.RunAsync("dwebp", ["-quiet", "-pam", candidatePath, "-o", candidatePixels], cancellationToken).ConfigureAwait(false);

    return await FilesEqualAsync(originalPixels, candidatePixels, cancellationToken).ConfigureAwait(false)
        ? VerificationResult.Identical
        : VerificationResult.Different;
  }

  internal static async Task<bool> FilesEqualAsync(string a, string b, CancellationToken cancellationToken)
  {
    await using var streamA = File.OpenRead(a);
    await using var streamB = File.OpenRead(b);
    if (streamA.Length != streamB.Length)
      return false;

    var bufferA = new byte[81920];
    var bufferB = new byte[81920];
    while (true)
    {
      var readA = await streamA.ReadAtLeastAsync(bufferA, bufferA.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
      var readB = await streamB.ReadAtLeastAsync(bufferB, bufferB.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
      if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
        return false;
      if (readA == 0)
        return true;
    }
  }
}

/// <summary>
/// Decodes AVIF files to raw planes (y4m) with avifdec and compares them byte for byte, after checking that
/// both containers describe the planes the same way (color space, depth, alpha, rotation, ...).
/// </summary>
public sealed class AvifPixelVerifier(IToolRunner tools) : IPixelVerifier
{
  public async Task<VerificationResult> VerifyAsync(ImageFormat format, string originalPath, string candidatePath, string workDirectory, CancellationToken cancellationToken)
  {
    if (format != ImageFormat.Avif)
      return VerificationResult.NotSupported;

    var original = AvifInspector.Inspect(await File.ReadAllBytesAsync(originalPath, cancellationToken).ConfigureAwait(false));
    var candidate = AvifInspector.Inspect(await File.ReadAllBytesAsync(candidatePath, cancellationToken).ConfigureAwait(false));
    if (!original.SameAppearance(candidate))
      return VerificationResult.Different;

    var id = Guid.NewGuid().ToString("N");
    var originalPixels = Path.Combine(workDirectory, $"verify-{id}-a.y4m");
    var candidatePixels = Path.Combine(workDirectory, $"verify-{id}-b.y4m");
    await tools.RunAsync("avifdec", ["--jobs", "all", originalPath, originalPixels], cancellationToken).ConfigureAwait(false);
    await tools.RunAsync("avifdec", ["--jobs", "all", candidatePath, candidatePixels], cancellationToken).ConfigureAwait(false);

    return await WebPPixelVerifier.FilesEqualAsync(originalPixels, candidatePixels, cancellationToken).ConfigureAwait(false)
        ? VerificationResult.Identical
        : VerificationResult.Different;
  }
}

/// <summary>
/// Decodes JPEG XL files to 32-bit float samples (NumPy files, so nothing is rounded or dithered) and their
/// ICC profiles with djxl, and compares them byte for byte, after checking that both headers describe the
/// image the same way. Recompressed JPEGs must also rebuild exactly the same JPEG file.
/// </summary>
public sealed class JxlPixelVerifier(IToolRunner tools) : IPixelVerifier
{
  public async Task<VerificationResult> VerifyAsync(ImageFormat format, string originalPath, string candidatePath, string workDirectory, CancellationToken cancellationToken)
  {
    if (format != ImageFormat.JpegXl)
      return VerificationResult.NotSupported;

    var original = JxlInspector.Inspect(await File.ReadAllBytesAsync(originalPath, cancellationToken).ConfigureAwait(false));
    var candidate = JxlInspector.Inspect(await File.ReadAllBytesAsync(candidatePath, cancellationToken).ConfigureAwait(false));
    if (!original.SameAppearance(candidate) || original.HasJpegReconstruction != candidate.HasJpegReconstruction)
      return VerificationResult.Different;

    var id = Guid.NewGuid().ToString("N");
    var outputs = new List<(string A, string B)>();
    if (original.HasJpegReconstruction)
    {
      var jpegs = (Path.Combine(workDirectory, $"verify-{id}-a.jpg"), Path.Combine(workDirectory, $"verify-{id}-b.jpg"));
      await tools.RunAsync("djxl", [originalPath, jpegs.Item1, "--reconstruct_jpeg", "--quiet"], cancellationToken).ConfigureAwait(false);
      await tools.RunAsync("djxl", [candidatePath, jpegs.Item2, "--reconstruct_jpeg", "--quiet"], cancellationToken).ConfigureAwait(false);
      outputs.Add(jpegs);
    }

    var pixels = (Path.Combine(workDirectory, $"verify-{id}-a.npy"), Path.Combine(workDirectory, $"verify-{id}-b.npy"));
    var profiles = (Path.Combine(workDirectory, $"verify-{id}-a.icc"), Path.Combine(workDirectory, $"verify-{id}-b.icc"));
    await tools.RunAsync("djxl", [originalPath, pixels.Item1, "--icc_out", profiles.Item1, "--quiet"], cancellationToken).ConfigureAwait(false);
    await tools.RunAsync("djxl", [candidatePath, pixels.Item2, "--icc_out", profiles.Item2, "--quiet"], cancellationToken).ConfigureAwait(false);
    outputs.Add(pixels);
    outputs.Add(profiles);

    foreach (var (a, b) in outputs)
    {
      if (!await WebPPixelVerifier.FilesEqualAsync(a, b, cancellationToken).ConfigureAwait(false))
        return VerificationResult.Different;
    }
    return VerificationResult.Identical;
  }
}
