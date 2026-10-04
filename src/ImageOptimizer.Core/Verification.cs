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
