namespace ImageOptimizer.Core.Tests;

public class GifTests : IDisposable
{
  private readonly TestFiles _files = new();
  private readonly ToolRunner _tools = new();

  public void Dispose() => _files.Dispose();

  private static byte[] Fixture => File.ReadAllBytes(TestFiles.FixturePath("animation.gif"));

  private static VerificationResult Compare(byte[] original, byte[] candidate) =>
      GifPixelVerifier.Compare(original, candidate, TestContext.Current.CancellationToken);

  private static int IndexOf(byte[] data, ReadOnlySpan<byte> value) => data.AsSpan().IndexOf(value);

  /// <summary>Adds a color profile extension straight after the global color table.</summary>
  private static byte[] WithColorProfile(byte[] gif)
  {
    var tableEnd = 13 + 3 * (2 << (gif[10] & 7));
    byte[] extension = [0x21, 0xFF, 11, .. "ICCRGBG1012"u8, 4, 1, 2, 3, 4, 0];
    return [.. gif[..tableEnd], .. extension, .. gif[tableEnd..]];
  }

  [Fact]
  public void Decoder_draws_every_frame_and_reads_the_loop_count()
  {
    var decoder = new GifDecoder(Fixture);
    var frames = 0;
    while (decoder.ReadFrame())
    {
      frames++;
      Assert.Contains(decoder.Canvas, pixel => pixel != 0);
    }

    Assert.Equal(3, frames);
    Assert.Equal((48, 48), (decoder.Width, decoder.Height));
    Assert.Equal(0, decoder.LoopCount);
    Assert.Contains("NETSCAPE2.0", decoder.ApplicationExtensions);
  }

  [Fact]
  public void Same_gif_is_identical() => Assert.Equal(VerificationResult.Identical, Compare(Fixture, Fixture));

  [Fact]
  public void Changed_colors_are_caught()
  {
    var changed = Fixture;
    for (var i = 13; i < 13 + 6; i++)
      changed[i] ^= 0x80;

    Assert.Equal(VerificationResult.Different, Compare(Fixture, changed));
  }

  [Fact]
  public void Changed_frame_delay_is_caught()
  {
    var changed = Fixture;
    changed[IndexOf(changed, [0x21, 0xF9, 0x04]) + 4] ^= 0x01;

    Assert.Equal(VerificationResult.Different, Compare(Fixture, changed));
  }

  [Fact]
  public void Changed_loop_count_is_caught()
  {
    var changed = Fixture;
    changed[IndexOf(changed, "NETSCAPE2.0"u8) + 13] = 5;

    Assert.Equal(VerificationResult.Different, Compare(Fixture, changed));
  }

  [Fact]
  public void Damaged_files_never_pass()
  {
    var damaged = Fixture[..40];

    Assert.Equal(VerificationResult.Different, Compare(Fixture, damaged));
    Assert.Equal(VerificationResult.Different, Compare(damaged, Fixture));
  }

  [Fact]
  public void Files_too_large_to_draw_never_pass()
  {
    var huge = Gif(5000, 5000, Frame(0, [1]));

    Assert.Equal(VerificationResult.Different, Compare(huge, huge));
  }

  [Fact]
  public async Task Damaged_gif_is_skipped_before_gifsicle_runs()
  {
    var input = _files.Write("input.gif", Fixture[..40]);
    var context = new OptimizationContext(input, _files.Directory, new OptimizerSettings());

    var set = await new GifOptimizer(_tools).CreateCandidatesAsync(context, TestContext.Current.CancellationToken);

    Assert.NotNull(set.SkipReason);
    Assert.Empty(set.Candidates);
  }

  [Fact]
  public void Color_profile_is_found()
  {
    Assert.False(GifOptimizer.HasColorProfile(GifDecoder.ReadApplicationExtensions(Fixture)));
    Assert.True(GifOptimizer.HasColorProfile(GifDecoder.ReadApplicationExtensions(WithColorProfile(Fixture))));
  }

  // Colors in the small GIFs below: 0 black, 1 red, 2 green, 3 blue.
  private const uint Red = 0xFFFF0000, Green = 0xFF00FF00, Blue = 0xFF0000FF;

  /// <summary>A GIF with a four-color palette and the given frames.</summary>
  private static byte[] Gif(int width, int height, params byte[][] frames) =>
      [.. "GIF89a"u8, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0x81, 0, 0,
       0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, .. frames.SelectMany(f => f), 0x3B];

  /// <summary>
  /// A frame at (x, 0), one pixel high, with the given disposal method. The pixels are LZW coded with a clear code
  /// before each one, so the code table never grows and every code is 3 bits.
  /// </summary>
  private static byte[] Frame(int disposal, byte[] pixels, int x = 0)
  {
    var codes = pixels.SelectMany(p => new[] { 4, (int)p }).Append(5).ToList();
    var data = new List<byte>();
    int bits = 0, count = 0;
    foreach (var code in codes)
    {
      bits |= code << count;
      count += 3;
      while (count >= 8)
      {
        data.Add((byte)bits);
        bits >>= 8;
        count -= 8;
      }
    }
    if (count > 0)
      data.Add((byte)bits);
    return [0x21, 0xF9, 4, (byte)(disposal << 2), 10, 0, 0, 0,
            0x2C, (byte)x, 0, 0, 0, (byte)pixels.Length, 0, 1, 0, 0,
            2, (byte)data.Count, .. data, 0];
  }

  private static List<uint[]> DrawnFrames(byte[] gif)
  {
    var decoder = new GifDecoder(gif);
    var frames = new List<uint[]>();
    while (decoder.ReadFrame())
      frames.Add([.. decoder.Canvas]);
    return frames;
  }

  [Fact]
  public void Restore_to_background_clears_the_frame()
  {
    var frames = DrawnFrames(Gif(2, 1, Frame(2, [1, 1]), Frame(0, [2], x: 1)));

    Assert.Equal([Red, Red], frames[0]);
    Assert.Equal([0u, Green], frames[1]);
  }

  [Fact]
  public void Restore_to_previous_brings_back_the_canvas_before_the_frame()
  {
    var frames = DrawnFrames(Gif(2, 1, Frame(0, [1, 1]), Frame(3, [3]), Frame(0, [2], x: 1)));

    Assert.Equal([Red, Red], frames[0]);
    Assert.Equal([Blue, Red], frames[1]);
    Assert.Equal([Red, Green], frames[2]);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(3)]
  public void Changed_disposal_is_caught(int disposal)
  {
    // Clearing the blue pixel, keeping it and restoring the red under it all end differently.
    var original = Gif(2, 1, Frame(0, [1, 1]), Frame(2, [3]), Frame(0, [2], x: 1));
    var changed = Gif(2, 1, Frame(0, [1, 1]), Frame(disposal, [3]), Frame(0, [2], x: 1));

    Assert.Equal(VerificationResult.Different, Compare(original, changed));
  }

  [Fact]
  public void Same_animation_stored_differently_is_identical()
  {
    // The second frame either repaints the whole canvas or only the pixel that changes.
    var full = Gif(2, 1, Frame(0, [1, 1]), Frame(0, [1, 2]));
    var cropped = Gif(2, 1, Frame(1, [1, 1]), Frame(0, [2], x: 1));

    Assert.Equal(VerificationResult.Identical, Compare(full, cropped));
  }

  [Theory]
  [InlineData("-O3")]
  [InlineData("--interlace")]
  [InlineData("--unoptimize")]
  public async Task Gifsicle_output_is_identical(string option)
  {
    ToolRequirements.Require(_tools, "gifsicle");
    var input = _files.CopyFixture("animation.gif");
    var output = Path.Combine(_files.Directory, "out.gif");
    await _tools.RunAsync("gifsicle", [option, input, "-o", output], TestContext.Current.CancellationToken);

    Assert.Equal(VerificationResult.Identical, Compare(File.ReadAllBytes(input), File.ReadAllBytes(output)));
  }

  [Theory]
  [InlineData(false, new[] { "NETSCAPE2.0" })]
  [InlineData(true, new[] { "NETSCAPE2.0", "ICCRGBG1012", "ImageMagick" })]
  public async Task Removing_metadata_drops_other_programs_data_but_keeps_a_color_profile(bool colorProfile, string[] kept)
  {
    ToolRequirements.Require(_tools, "gifsicle");
    var input = _files.Write("input.gif", colorProfile ? WithColorProfile(Fixture) : Fixture);
    var context = new OptimizationContext(input, _files.Directory, new OptimizerSettings());

    var set = await new GifOptimizer(_tools).CreateCandidatesAsync(context, TestContext.Current.CancellationToken);

    var output = File.ReadAllBytes(Assert.Single(set.Candidates).Path);
    Assert.Equal(kept.Order(), GifDecoder.ReadApplicationExtensions(output).Distinct().Order());
    Assert.Equal(VerificationResult.Identical, Compare(File.ReadAllBytes(input), output));
  }

  [Fact]
  public async Task Keeping_metadata_keeps_other_programs_data()
  {
    ToolRequirements.Require(_tools, "gifsicle");
    var input = _files.CopyFixture("animation.gif");
    var context = new OptimizationContext(input, _files.Directory, new OptimizerSettings { StripMetadata = false });

    var set = await new GifOptimizer(_tools).CreateCandidatesAsync(context, TestContext.Current.CancellationToken);

    Assert.Contains("ImageMagick", GifDecoder.ReadApplicationExtensions(File.ReadAllBytes(Assert.Single(set.Candidates).Path)));
  }

  [Fact]
  public async Task Engine_rejects_a_gif_whose_pixels_changed()
  {
    var path = _files.CopyFixture("animation.gif");
    var before = File.ReadAllBytes(path);
    var engine = new ImageOptimizerEngine([new ChangingGifOptimizer()], new CompositeVerifier(new GifPixelVerifier()), _files.Directory);

    var result = await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Failed, result.Status);
    Assert.Equal(before, File.ReadAllBytes(path));
  }

  /// <summary>Writes a smaller GIF with its first color changed, as a buggy optimizer might.</summary>
  private sealed class ChangingGifOptimizer : IFormatOptimizer
  {
    public ImageFormat Format => ImageFormat.Gif;

    public Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
    {
      var data = File.ReadAllBytes(context.InputPath);
      for (var i = 13; i < 13 + 6; i++)
        data[i] ^= 0x80;
      var output = context.WorkFile("changed.gif");
      // Leaving off the trailer makes it a byte smaller; browsers (and the verifier) don't need it.
      File.WriteAllBytes(output, data[..^1]);
      return Task.FromResult(new CandidateSet([new Candidate(output)]));
    }
  }
}
