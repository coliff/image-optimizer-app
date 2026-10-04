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
    Assert.Empty(Directory.GetFiles(_files.Directory, "*.tmp"));
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
  public async Task Preserves_modified_date_when_asked()
  {
    var path = _files.CopyFixture("unoptimized.png");
    var date = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(path, date);
    var engine = Engine(ctx => new([WriteCandidate(ctx, "a.png", 100)]));

    await engine.OptimizeAsync(path, new OptimizerSettings { PreserveModifiedDate = true }, TestContext.Current.CancellationToken);

    Assert.Equal(date, File.GetLastWriteTimeUtc(path));
  }
}
