namespace ImageOptimizer.Core.Tests;

/// <summary>Exercises the replace-only-if-smaller-and-identical rules with fake optimizers.</summary>
public class EngineTests : IDisposable
{
  private readonly TestFiles _files = new();

  public void Dispose() => _files.Dispose();

  private sealed class FakeOptimizer(Func<OptimizationContext, CandidateSet> produce) : IFormatOptimizer
  {
    public ImageFormat Format => ImageFormat.Png;

    public Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken) =>
        Task.FromResult(produce(context));
  }

  private sealed class FakeVerifier(VerificationResult result) : IPixelVerifier
  {
    public int Calls { get; private set; }

    public Task<VerificationResult> VerifyAsync(ImageFormat format, string originalPath, string candidatePath, string workDirectory, CancellationToken cancellationToken)
    {
      Calls++;
      return Task.FromResult(result);
    }
  }

  private ImageOptimizerEngine Engine(Func<OptimizationContext, CandidateSet> produce, IPixelVerifier? verifier = null) =>
      new([new FakeOptimizer(produce)], verifier ?? new FakeVerifier(VerificationResult.Identical), Path.Combine(_files.Directory, "work"));

  private static Candidate WriteCandidate(OptimizationContext context, string name, int size)
  {
    var bytes = File.ReadAllBytes(context.InputPath)[..8].Concat(new byte[size - 8]).ToArray();
    var path = context.WorkFile(name);
    File.WriteAllBytes(path, bytes);
    return new Candidate(path);
  }

  [Fact]
  public async Task Replaces_original_with_smallest_candidate()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var engine = Engine(ctx => new([WriteCandidate(ctx, "a.png", 900), WriteCandidate(ctx, "b.png", 500)]));

    var result = await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    Assert.Equal(500, result.FinalSize);
    Assert.Equal(500, new FileInfo(path).Length);
    Assert.Equal([path], Directory.GetFiles(_files.Directory));
  }

  [Fact]
  public void Staging_copies_keep_the_image_extension_and_are_not_collected()
  {
    var original = new FileInfo(_files.CopyFixture("unoptimized.png"));

    var staging = ImageOptimizerEngine.StagingPath(original);

    Assert.Equal(original.DirectoryName, Path.GetDirectoryName(staging));
    Assert.Equal(".png", Path.GetExtension(staging));
    Assert.StartsWith("unoptimized.imageoptimizer-", Path.GetFileName(staging));
    Assert.True(ImageOptimizerEngine.IsStagingFile(staging));
    Assert.False(ImageOptimizerEngine.IsStagingFile(original.FullName));

    File.Copy(original.FullName, staging);
    Assert.Equal([original.FullName], FileCollector.Collect([_files.Directory, staging]));
  }

  [Fact]
  public async Task Leaves_original_untouched_when_candidate_is_not_smaller()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var before = File.ReadAllBytes(path);
    var engine = Engine(ctx => new([WriteCandidate(ctx, "a.png", before.Length), WriteCandidate(ctx, "b.png", before.Length + 50)]));

    var result = await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.AlreadyOptimized, result.Status);
    Assert.Equal(before, File.ReadAllBytes(path));
  }

  [Fact]
  public async Task Discards_candidates_whose_pixels_differ()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var before = File.ReadAllBytes(path);
    var engine = Engine(ctx => new([WriteCandidate(ctx, "a.png", 100)]), new FakeVerifier(VerificationResult.Different));

    var result = await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Failed, result.Status);
    Assert.Equal(before, File.ReadAllBytes(path));
  }

  [Fact]
  public async Task Skips_verification_for_container_only_changes()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var verifier = new FakeVerifier(VerificationResult.Different);
    var engine = Engine(ctx => new([WriteCandidate(ctx, "a.png", 100) with { PixelDataUntouched = true }]), verifier);

    var result = await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    Assert.Equal(0, verifier.Calls);
  }

  [Fact]
  public async Task Tool_failures_leave_original_untouched()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var before = File.ReadAllBytes(path);
    var engine = Engine(_ => throw new ToolFailedException("oxipng", 1, "boom"));

    var result = await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Failed, result.Status);
    Assert.Contains("boom", result.Message);
    Assert.Equal(before, File.ReadAllBytes(path));
    Assert.Empty(Directory.GetDirectories(Path.Combine(_files.Directory, "work")));
  }

  [Fact]
  public async Task Does_not_overwrite_a_file_that_changed_meanwhile()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var engine = Engine(ctx =>
    {
      File.AppendAllText(path, "edited by someone else");
      return new([WriteCandidate(ctx, "a.png", 100)]);
    });

    var result = await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Failed, result.Status);
    Assert.EndsWith("edited by someone else", File.ReadAllText(path));
  }

  [Fact]
  public async Task Respects_disabled_formats_and_unknown_files()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var engine = Engine(ctx => new([WriteCandidate(ctx, "a.png", 100)]));

    var disabled = await engine.OptimizeAsync(path, new OptimizerSettings { OptimizePng = false }, TestContext.Current.CancellationToken);
    Assert.Equal(OptimizationStatus.Skipped, disabled.Status);

    var fake = _files.Write("fake.png", "hello"u8.ToArray());
    Assert.Equal(OptimizationStatus.Skipped, (await engine.OptimizeAsync(fake, new OptimizerSettings(), TestContext.Current.CancellationToken)).Status);

    var missing = await engine.OptimizeAsync(Path.Combine(_files.Directory, "gone.png"), new OptimizerSettings(), TestContext.Current.CancellationToken);
    Assert.Equal(OptimizationStatus.Failed, missing.Status);
  }

  [Fact]
  public async Task Preserves_modified_date_by_default()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var date = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(path, date);
    var engine = Engine(ctx => new([WriteCandidate(ctx, "a.png", 100)]));

    await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(date, File.GetLastWriteTimeUtc(path));
  }

  [Fact]
  public async Task Updates_modified_date_when_not_preserved()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var date = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(path, date);
    var engine = Engine(ctx => new([WriteCandidate(ctx, "a.png", 100)]));

    await engine.OptimizeAsync(path, new OptimizerSettings { PreserveModifiedDate = false }, TestContext.Current.CancellationToken);

    Assert.NotEqual(date, File.GetLastWriteTimeUtc(path));
  }

  [Fact]
  public void A_modified_date_that_cannot_be_restored_is_not_an_error()
  {
    var missing = Path.Combine(_files.Directory, "gone.png");

    Assert.False(ImageOptimizerEngine.TryRestoreModifiedDate(missing, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
  }

  [Fact]
  public void Leftovers_from_a_crash_are_cleaned_up_but_recent_ones_are_kept()
  {
    var workRoot = Path.Combine(_files.Directory, "work");
    var engine = Engine(ctx => new([]));
    var old = DateTime.UtcNow.AddDays(-2);

    var oldFolder = Directory.CreateDirectory(Path.Combine(workRoot, "old")).FullName;
    File.WriteAllText(Path.Combine(oldFolder, "input.png"), "x");
    Directory.SetLastWriteTimeUtc(oldFolder, old);
    var newFolder = Directory.CreateDirectory(Path.Combine(workRoot, "new")).FullName;

    var original = new FileInfo(_files.CopyFixture("unoptimized.png"));
    var staging = ImageOptimizerEngine.StagingPath(original);
    File.WriteAllText(staging, "half-written");
    var pending = Directory.CreateDirectory(Path.Combine(workRoot, "pending")).FullName;
    var record = Path.Combine(pending, "a.txt");
    File.WriteAllText(record, staging);
    File.SetLastWriteTimeUtc(record, old);

    // A note that points at something other than a staging copy must never delete it.
    var bogus = Path.Combine(pending, "b.txt");
    File.WriteAllText(bogus, original.FullName);
    File.SetLastWriteTimeUtc(bogus, old);

    engine.CleanUpLeftovers(TimeSpan.FromDays(1));

    Assert.False(Directory.Exists(oldFolder));
    Assert.True(Directory.Exists(newFolder));
    Assert.False(File.Exists(staging));
    Assert.False(File.Exists(record));
    Assert.True(File.Exists(original.FullName));
    Assert.False(File.Exists(bogus));
  }

  [Fact]
  public async Task Optimizing_leaves_no_staging_notes_behind()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var engine = Engine(ctx => new([WriteCandidate(ctx, "a.png", 100)]));

    await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    var pending = Path.Combine(_files.Directory, "work", "pending");
    Assert.Empty(Directory.Exists(pending) ? Directory.GetFiles(pending) : []);
  }

  [Theory]
  [InlineData(ImageFormat.Avif, true, true)]
  [InlineData(ImageFormat.JpegXl, true, true)]
  [InlineData(ImageFormat.Png, true, false)]
  [InlineData(ImageFormat.Avif, false, false)]
  public void Only_maximum_compression_avif_and_jpeg_xl_take_turns(ImageFormat format, bool maximum, bool heavy) =>
      Assert.Equal(heavy, ImageOptimizerEngine.IsHeavy(format, new OptimizerSettings { MaximumCompression = maximum }));
}
