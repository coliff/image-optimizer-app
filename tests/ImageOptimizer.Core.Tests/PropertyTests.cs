using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace ImageOptimizer.Core.Tests;

/// <summary>
/// Property-based fuzzing with FsCheck: random and damaged files must never crash the parsers or the engine,
/// and must never leave the original bigger or changed unless a verified, strictly smaller candidate replaced it.
/// </summary>
public class PropertyTests
{
  private static readonly string[] Fixtures =
  [
    "unoptimized.png", "optimized.png", "photo.jpg", "lossless.webp", "lossy.webp", "lossless.avif", "lossy.avif",
    "animation.gif", "lossless.jxl", "lossy.jxl", "animated.jxl", "recompressed-jpeg.jxl", "drawing.svg",
  ];

  private static readonly Lazy<byte[][]> FixtureBytes =
      new(() => [.. Fixtures.Select(name => File.ReadAllBytes(TestFiles.FixturePath(name)))]);

  // Enough of each signature for the format checks to pass, so random data reaches the parsers behind them.
  private static readonly byte[][] Signatures =
  [
    [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
    [0xFF, 0xD8, 0xFF],
    [.. "RIFF\0\0\0\0WEBP"u8],
    [0, 0, 0, 0x1C, .. "ftyp"u8, .. "avif"u8, 0, 0, 0, 0, .. "avif"u8, .. "mif1"u8],
    [0, 0, 0, 0x0C, .. "JXL \r\n\x87\n"u8],
    [0xFF, 0x0A],
    [.. "GIF89a"u8],
    [.. "<svg xmlns=\"http://www.w3.org/2000/svg\">"u8],
  ];

  /// <summary>A real image with random bytes overwritten, inserted or cut off, or random data behind a real signature.</summary>
  public sealed record DamagedImage(byte[] Data)
  {
    public override string ToString() => $"DamagedImage({Data.Length} bytes: {Convert.ToHexString(Data.AsSpan(0, Math.Min(Data.Length, 64)))}...)";
  }

  public static class Arbitraries
  {
    public static Arbitrary<DamagedImage> DamagedImage() => DamagedImages.ToArbitrary();
  }

  private static Gen<byte> AnyByte => Gen.Choose(0, 255).Select(b => (byte)b);

  private static Gen<T[]> ArrayOf<T>(Gen<T> gen, int min, int max) =>
      Gen.Choose(min, max).SelectMany(length => Gen.ArrayOf(gen, length));

  private static Gen<byte[]> RandomBytes(int max) => ArrayOf(AnyByte, 0, max);

  private static Gen<DamagedImage> DamagedImages =>
      Gen.OneOf(
          Gen.Choose(0, Fixtures.Length - 1).SelectMany(index => Damage(FixtureBytes.Value[index])),
          Gen.Elements(Signatures).SelectMany(signature => RandomBytes(512).Select(tail => (byte[])[.. signature, .. tail])))
      .Select(data => new DamagedImage(data));

  private static Gen<byte[]> Damage(byte[] original) =>
      ArrayOf(Edit(), 1, 8)
          .Select(edits => edits.Aggregate(original, (data, edit) => edit(data)));

  private static Gen<Func<byte[], byte[]>> Edit() =>
      Gen.OneOf(
          // Overwrite a few bytes, often landing in a length or offset field.
          Gen.Choose(0, int.MaxValue).SelectMany(at => RandomBytes(4).Select(bytes => (Func<byte[], byte[]>)(data =>
          {
            if (data.Length == 0)
              return data;
            var copy = (byte[])data.Clone();
            var start = at % copy.Length;
            for (var i = 0; i < bytes.Length && start + i < copy.Length; i++)
              copy[start + i] = bytes[i];
            return copy;
          }))),
          // Set a 32-bit field to an extreme value.
          Gen.Choose(0, int.MaxValue).SelectMany(at => Gen.Elements(0u, 1u, 7u, 8u, 0x7FFFFFFFu, 0xFFFFFFFFu).Select(value => (Func<byte[], byte[]>)(data =>
          {
            if (data.Length < 4)
              return data;
            var copy = (byte[])data.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(at % (copy.Length - 3)), value);
            return copy;
          }))),
          // Cut the file short.
          Gen.Choose(0, int.MaxValue).Select(at => (Func<byte[], byte[]>)(data => data[..(data.Length == 0 ? 0 : at % data.Length)])),
          // Insert random bytes.
          Gen.Choose(0, int.MaxValue).SelectMany(at => RandomBytes(32).Select(bytes => (Func<byte[], byte[]>)(data =>
          {
            var start = data.Length == 0 ? 0 : at % data.Length;
            return [.. data[..start], .. bytes, .. data[start..]];
          }))));

  private static void InspectEverything(byte[] data)
  {
    ImageFormats.Detect(data);
    ImageFormats.LooksLikeSvg(data);
    ImageInspector.IsAnimatedPng(data);
    ImageInspector.HasJpegTrailingData(data);
    Assert.True(ImageInspector.GetPngExifOrientation(data) is null or (>= 1 and <= 8));
    Assert.True(ImageInspector.GetJpegExifOrientation(data) is null or (>= 1 and <= 8));
    Assert.True(ImageInspector.InspectWebP(data).ExifOrientation is null or (>= 1 and <= 8));
    AvifInspector.HasAvifBrand(data);
    AvifInspector.Inspect(data);
    JxlInspector.HasSignature(data);
    JxlInspector.Inspect(data);
    try
    {
      GifDecoder.ReadApplicationExtensions(data);
    }
    catch (InvalidDataException)
    {
    }
    GifPixelVerifier.Compare(data, data, CancellationToken.None);
  }

  [Property(MaxTest = 1000)]
  public void Parsers_never_throw_on_random_bytes(byte[] data) => InspectEverything(data);

  [Property(MaxTest = 2000, Arbitrary = [typeof(Arbitraries)])]
  public void Parsers_never_throw_on_damaged_images(DamagedImage image) => InspectEverything(image.Data);

  [Property(Arbitrary = [typeof(Arbitraries)])]
  public void Detect_agrees_for_bytes_and_files(DamagedImage image)
  {
    using var files = new TestFiles();
    var path = files.Write("image.bin", image.Data);
    Assert.Equal(ImageFormats.Detect(image.Data.AsSpan(0, Math.Min(image.Data.Length, 64))), ImageFormats.Detect(path));
  }

  /// <summary>What a fake optimizer hands back: its size, whether the pixels are untouched and what the verifier says.</summary>
  public sealed record FakeCandidate(int Size, bool PixelDataUntouched, VerificationResult Verdict);

  private sealed class FakeOptimizer(ImageFormat format, IReadOnlyList<FakeCandidate> candidates, Dictionary<string, FakeCandidate> written) : IFormatOptimizer
  {
    public ImageFormat Format => format;

    public Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
    {
      var result = new List<Candidate>();
      for (var i = 0; i < candidates.Count; i++)
      {
        var path = context.WorkFile($"candidate{i}");
        // Each candidate's bytes say which one it was, so the test can tell which one replaced the original.
        var bytes = new byte[candidates[i].Size];
        if (bytes.Length > 0)
          bytes[0] = (byte)i;
        File.WriteAllBytes(path, bytes);
        written[path] = candidates[i];
        result.Add(new Candidate(path, candidates[i].PixelDataUntouched));
      }
      return Task.FromResult(new CandidateSet(result));
    }
  }

  private sealed class FakeVerifier(Dictionary<string, FakeCandidate> written) : IPixelVerifier
  {
    public Task<VerificationResult> VerifyAsync(ImageFormat format, string originalPath, string candidatePath, string workDirectory, CancellationToken cancellationToken) =>
        Task.FromResult(written[candidatePath].Verdict);
  }

  [Property(MaxTest = 300, Arbitrary = [typeof(Arbitraries)])]
  public void Engine_only_replaces_a_file_with_a_smaller_verified_candidate(DamagedImage image, FakeCandidate[] candidates, bool maximumCompression)
  {
    candidates = [.. candidates.Select(c => c with { Size = Math.Abs(c.Size % (image.Data.Length + 64)) })];
    using var files = new TestFiles();
    var extension = ImageFormats.Detect(image.Data) == ImageFormat.Unknown ? ".svg" : ".img";
    var path = files.Write("image" + extension, image.Data);
    var written = new Dictionary<string, FakeCandidate>();
    var optimizers = Enum.GetValues<ImageFormat>().Where(f => f != ImageFormat.Unknown).Select(f => new FakeOptimizer(f, candidates, written));
    var engine = new ImageOptimizerEngine(optimizers, new FakeVerifier(written), Path.Combine(files.Directory, "work"));

    var result = engine.OptimizeAsync(path, new OptimizerSettings { MaximumCompression = maximumCompression }, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    var after = File.ReadAllBytes(path);
    var winner = candidates
        .Select((c, i) => (Candidate: c, Index: i))
        .Where(c => c.Candidate.Size > 0 && c.Candidate.Size < image.Data.Length)
        .Where(c => c.Candidate.PixelDataUntouched || c.Candidate.Verdict != VerificationResult.Different)
        .OrderBy(c => c.Candidate.Size)
        .Select(c => ((FakeCandidate, int)?)c)
        .FirstOrDefault();

    if (result.Status == OptimizationStatus.Optimized)
    {
      Assert.NotNull(winner);
      Assert.Equal(winner.Value.Item1.Size, after.Length);
      Assert.True(after.Length < image.Data.Length);
      Assert.Equal(after.Length, result.FinalSize);
    }
    else
    {
      Assert.Equal(image.Data, after);
      Assert.Equal(result.OriginalSize, result.FinalSize);
    }
    Assert.Equal([path], Directory.GetFiles(files.Directory));
  }

  // Elements, attributes and text that SVGO has to parse, escape or rewrite, including some that break the XML.
  private static readonly string[] SvgTags = ["g", "path", "rect", "circle", "text", "defs", "use", "style", "title", "metadata", "foreignObject", "a:b", "svg"];
  private static readonly string[] SvgAttributes = ["fill=\"red\"", "d=\"M0 0L10 10z\"", "x=\"1e400\"", "transform=\"rotate(45 0 0)\"", "id=\"a\"", "href=\"#a\"", "style=\"fill:#fff;;\"", "xmlns:a=\"urn:a\"", "a=\"&amp;&lt;\"", "b='\"'", "c=\"&bogus;\"", "d=\"😀\""];
  private static readonly string[] SvgText = ["", "hello", "&amp;", "&", "<", "]]>", "<![CDATA[x]]>", "<!-- c -->", "<?pi x?>", "é日本", "\t\n ", "</g>", "<!DOCTYPE svg>"];

  private static Gen<string> SvgNode(int depth) =>
      depth <= 0
          ? Gen.Elements(SvgText)
          : Gen.OneOf(
              Gen.Elements(SvgText),
              from tag in Gen.Elements(SvgTags)
              from attributes in Gen.SubListOf(SvgAttributes)
              from children in ArrayOf(SvgNode(depth - 1), 0, 4)
              select $"<{tag} {string.Join(' ', attributes)}>{string.Concat(children)}</{tag}>");

  private static Gen<string> RandomSvgs =>
      from body in ArrayOf(SvgNode(4), 0, 5)
      from cut in Gen.Choose(0, 100)
      let svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">{string.Concat(body)}</svg>"
      // Now and then cut the document short.
      select cut < 15 ? svg[..(svg.Length * cut / 15)] + "<svg" : svg;

  [Fact]
  public async Task Svgo_never_crashes_on_random_svgs_and_keeps_failures_untouched()
  {
    var tools = new ToolRunner();
    ToolRequirements.RequireFile(tools, SvgOptimizer.ScriptName);
    var engine = ImageOptimizerEngine.CreateDefault(tools);
    using var files = new TestFiles();

    var samples = Gen.Sample(RandomSvgs, 60);
    foreach (var (svg, i) in samples.Select((s, i) => (s, i)))
    {
      var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
      var path = files.Write($"random{i}.svg", bytes);

      var result = await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

      var after = File.ReadAllBytes(path);
      if (result.Status == OptimizationStatus.Optimized)
      {
        Assert.True(after.Length < bytes.Length, svg);
        Assert.Contains("<svg", System.Text.Encoding.UTF8.GetString(after), StringComparison.Ordinal);
      }
      else
      {
        Assert.Equal(bytes, after);
      }
    }
  }
}
