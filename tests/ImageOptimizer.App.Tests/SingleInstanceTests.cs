namespace ImageOptimizer.App.Tests;

public class SingleInstanceTests
{
  [Fact]
  public async Task Later_copies_hand_their_files_to_the_first()
  {
    var name = $"ImageOptimizerTests.{Guid.NewGuid():N}";
    using var first = new SingleInstance(name);
    Assert.True(first.IsFirst);

    var received = new System.Collections.Concurrent.ConcurrentBag<string>();
    var all = new TaskCompletionSource();
    first.Listen();
    first.OnReceived(paths =>
    {
      foreach (var path in paths)
        received.Add(path);
      if (received.Count == 3)
        all.TrySetResult();
    });

    // Explorer starts one copy per selected file, all at once.
    var files = new[] { @"C:\images\a.png", @"C:\images\b.jpg", @"C:\images\c d.webp" };
    var sends = files.Select(file => Task.Run(() =>
    {
      using var later = new SingleInstance(name);
      Assert.False(later.IsFirst);
      return later.TrySend([file], TimeSpan.FromSeconds(10));
    })).ToList();

    Assert.All(await Task.WhenAll(sends), Assert.True);
    await all.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    Assert.Equal(files.Order(), received.Order());
  }

  [Fact]
  public async Task Files_sent_while_the_first_copy_is_still_starting_are_kept()
  {
    var name = $"ImageOptimizerTests.{Guid.NewGuid():N}";
    using var first = new SingleInstance(name);
    // Listening starts straight away; the window (and its handler) comes later.
    first.Listen();

    var sent = await Task.Run(() =>
    {
      using var later = new SingleInstance(name);
      return later.TrySend([@"C:\images\early.png"], TimeSpan.FromSeconds(10));
    }, TestContext.Current.CancellationToken);
    Assert.True(sent);

    var received = new TaskCompletionSource<string[]>();
    // Give the first copy time to read the message before anything handles it.
    await Task.Delay(500, TestContext.Current.CancellationToken);
    first.OnReceived(paths => received.TrySetResult(paths));

    var paths = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    Assert.Equal([@"C:\images\early.png"], paths);
  }

  [Fact]
  public void Sending_fails_when_nothing_is_listening()
  {
    using var lonely = new SingleInstance($"ImageOptimizerTests.{Guid.NewGuid():N}");
    Assert.False(lonely.TrySend([@"C:\a.png"], TimeSpan.FromMilliseconds(200)));
  }
}
