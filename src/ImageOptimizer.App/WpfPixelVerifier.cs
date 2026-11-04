using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageOptimizer.Core;

namespace ImageOptimizer.App;

/// <summary>
/// Decodes PNG and JPEG files, and the PNG images inside icons, with Windows' own codecs and checks every pixel
/// of the optimized file against the original, as a final guard that an optimization really was lossless.
/// </summary>
public sealed class WpfPixelVerifier : IPixelVerifier
{
  private const int RowsPerBlock = 64;

  public Task<VerificationResult> VerifyAsync(ImageFormat format, string originalPath, string candidatePath, string workDirectory, CancellationToken cancellationToken)
  {
    return format switch
    {
      ImageFormat.Png or ImageFormat.Jpeg => Task.Run(() => CompareFiles(originalPath, candidatePath, cancellationToken), cancellationToken),
      ImageFormat.Ico => Task.Run(() => CompareIcons(originalPath, candidatePath, cancellationToken), cancellationToken),
      _ => Task.FromResult(VerificationResult.NotSupported),
    };
  }

  private static VerificationResult CompareFiles(string originalPath, string candidatePath, CancellationToken cancellationToken)
  {
    using var originalStream = File.OpenRead(originalPath);
    using var candidateStream = File.OpenRead(candidatePath);
    return Compare(originalStream, candidateStream, cancellationToken);
  }

  /// <summary>
  /// Every image must still be there in the same order with the same directory entry; BMP images must be byte for
  /// byte the same, and PNG images must decode to the same pixels.
  /// </summary>
  private static VerificationResult CompareIcons(string originalPath, string candidatePath, CancellationToken cancellationToken)
  {
    var original = IcoFile.Parse(File.ReadAllBytes(originalPath));
    if (original is null)
      return VerificationResult.NotSupported;
    var candidate = IcoFile.Parse(File.ReadAllBytes(candidatePath));
    if (candidate is null || candidate.Type != original.Type || candidate.Entries.Count != original.Entries.Count)
      return VerificationResult.Different;

    for (var i = 0; i < original.Entries.Count; i++)
    {
      var a = original.Entries[i];
      var b = candidate.Entries[i];
      // The first 8 bytes are the size, color count, planes or hotspot and bit count; the rest is size and offset.
      if (a.IsPng != b.IsPng || !a.Header.AsSpan(0, 8).SequenceEqual(b.Header.AsSpan(0, 8)))
        return VerificationResult.Different;
      if (!a.IsPng)
      {
        if (!a.Data.AsSpan().SequenceEqual(b.Data))
          return VerificationResult.Different;
        continue;
      }
      if (a.Data.AsSpan().SequenceEqual(b.Data))
        continue;

      using var originalStream = new MemoryStream(a.Data, writable: false);
      using var candidateStream = new MemoryStream(b.Data, writable: false);
      // Like a PNG file, an image Windows can't read in the original is left to the lossless tool.
      if (Compare(originalStream, candidateStream, cancellationToken) == VerificationResult.Different)
        return VerificationResult.Different;
    }
    return VerificationResult.Identical;
  }

  private static VerificationResult Compare(Stream originalStream, Stream candidateStream, CancellationToken cancellationToken)
  {
    BitmapSource original;
    try
    {
      original = Decode(originalStream);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Windows can't read the original, so there's nothing to compare against; trust the lossless tool.
      return VerificationResult.NotSupported;
    }

    BitmapSource candidate;
    try
    {
      candidate = Decode(candidateStream);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      return VerificationResult.Different;
    }

    if (original.PixelWidth != candidate.PixelWidth || original.PixelHeight != candidate.PixelHeight)
      return VerificationResult.Different;

    // Compare at 16 bits per channel so that bit-depth reductions are checked exactly.
    var a = new FormatConvertedBitmap(original, PixelFormats.Rgba64, null, 0);
    var b = new FormatConvertedBitmap(candidate, PixelFormats.Rgba64, null, 0);
    var width = a.PixelWidth;
    var height = a.PixelHeight;
    var stride = width * 8;
    var rows = Math.Min(RowsPerBlock, height);
    var bufferA = new byte[stride * rows];
    var bufferB = new byte[stride * rows];

    for (var y = 0; y < height; y += RowsPerBlock)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var count = Math.Min(RowsPerBlock, height - y);
      var rect = new Int32Rect(0, y, width, count);
      a.CopyPixels(rect, bufferA, stride, 0);
      b.CopyPixels(rect, bufferB, stride, 0);
      if (!bufferA.AsSpan(0, stride * count).SequenceEqual(bufferB.AsSpan(0, stride * count)))
        return VerificationResult.Different;
    }
    return VerificationResult.Identical;
  }

  private static BitmapSource Decode(Stream stream)
  {
    var decoder = BitmapDecoder.Create(
        stream,
        BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
        BitmapCacheOption.OnLoad);
    // Decode everything up front so a corrupt file fails here rather than halfway through the comparison.
    var frame = decoder.Frames[0];
    frame.Freeze();
    return frame;
  }
}
