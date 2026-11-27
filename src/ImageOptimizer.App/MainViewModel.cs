using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Windows.Data;
using ImageOptimizer.Core;

namespace ImageOptimizer.App;

/// <summary>Owns the file list and the background queue that optimizes it.</summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
  private readonly ImageOptimizerEngine _engine;
  private readonly Channel<FileItem> _queue = Channel.CreateUnbounded<FileItem>();
  private readonly CancellationTokenSource _shutdown = new();
  private readonly Dictionary<string, FileItem> _byPath = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<Task> _running = [];
  private int _pending;
  private int _scanning;
  private int _found;

  public MainViewModel(ImageOptimizerEngine engine, OptimizerSettings settings)
  {
    _engine = engine;
    Settings = settings;

    // The optimizers are multi-threaded themselves, so a few files at a time keeps every core busy.
    FilesView = (ListCollectionView)CollectionViewSource.GetDefaultView(Files);
    FilesView.IsLiveSorting = true;

    var workers = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    for (var i = 0; i < workers; i++)
      _ = RunWorkerAsync();
  }

  public event PropertyChangedEventHandler? PropertyChanged;

  public ObservableCollection<FileItem> Files { get; } = [];

  public OptimizerSettings Settings { get; set; }

  /// <summary>The list as shown, sorted by whichever column header was clicked last.</summary>
  public ListCollectionView FilesView { get; }

  public SortColumn? SortedBy { get; private set; }

  public bool SortDescending { get; private set; }

  public string FileHeader => Header(Strings.FileColumn, SortColumn.File);
  public string SizeHeader => Header(Strings.SizeColumn, SortColumn.Size);
  public string SavingsHeader => Header(Strings.SavingsColumn, SortColumn.Savings);

  /// <summary>Header names for screen readers, which read the sort arrows as symbols.</summary>
  public string FileHeaderName => HeaderName(Strings.FileColumn, SortColumn.File);
  public string SizeHeaderName => HeaderName(Strings.SizeColumn, SortColumn.Size);
  public string SavingsHeaderName => HeaderName(Strings.SavingsColumn, SortColumn.Savings);

  public bool IsEmpty => Files.Count == 0;

  public bool IsBusy => _pending > 0 || _scanning > 0;

  /// <summary>True while dropped folders are still being searched for images.</summary>
  public bool IsScanning => _scanning > 0;

  public string Summary
  {
    get
    {
      if (IsScanning)
        return Strings.FindingImages(_found);
      if (Files.Count == 0)
        return "";
      if (IsBusy)
      {
        var done = Files.Count(f => f.IsFinished);
        return Strings.Progress(done + 1, Files.Count);
      }

      var measured = Files
          .Where(f => f.Status is FileStatus.Optimized or FileStatus.AlreadyOptimized && f.Result is not null)
          .Select(f => f.Result!)
          .ToList();
      var failed = Files.Count(f => f.Status == FileStatus.Failed);
      var failedText = failed == 0 ? "" : Strings.FilesFailed(failed);

      var original = measured.Sum(r => r.OriginalSize);
      var saved = measured.Sum(r => r.BytesSaved);
      if (saved <= 0)
        return (measured.Count > 0 ? Strings.NothingToSave : Strings.NothingOptimized) + failedText;

      var best = measured.Max(r => r.SavingsRatio);
      return Strings.SavedTotal(SizeFormatter.Format(saved), SizeFormatter.Format(original),
                 SizeFormatter.Percent((double)saved / original), SizeFormatter.Percent(best)) +
             failedText;
    }
  }

  // How long the list takes in found images before the window gets a moment to draw them and handle input.
  private static readonly TimeSpan BatchTime = TimeSpan.FromMilliseconds(50);
  private static readonly TimeSpan BatchPause = TimeSpan.FromMilliseconds(15);

  /// <summary>
  /// Adds dropped files or folders and queues every image found in them. Folders are searched in the background
  /// and their images are added as they're found, so a large folder or a slow network share never freezes the window.
  /// Must be called on the UI thread.
  /// </summary>
  public async Task AddAsync(IEnumerable<string> paths)
  {
    var dropped = paths.ToArray();
    var settings = Settings;
    var token = _shutdown.Token;
    var found = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    if (_scanning++ == 0)
      _found = 0;
    RaiseStateChanged();

    _ = Task.Run(() =>
    {
      try
      {
        foreach (var path in FileCollector.Collect(dropped, settings.SkipNodeModules, settings.SkipOnlineOnlyFiles))
        {
          if (token.IsCancellationRequested)
            break;
          found.Writer.TryWrite(path);
        }
        found.Writer.TryComplete();
      }
      catch (Exception ex)
      {
        found.Writer.TryComplete(ex);
      }
    }, CancellationToken.None);

    try
    {
      while (await found.Reader.WaitToReadAsync(token))
      {
        var batch = Stopwatch.StartNew();
        while (batch.Elapsed < BatchTime && found.Reader.TryRead(out var path))
          AddFile(path);
        RaiseStateChanged();

        // More are waiting, so let the window draw the new rows and respond to clicks before taking the next batch.
        if (found.Reader.TryPeek(out _))
          await Task.Delay(BatchPause, token);
      }
    }
    catch (OperationCanceledException)
    {
      // The app is closing.
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
      // The search stopped partway (a path Windows can't open, for example); the images found so far stay in the list.
    }
    finally
    {
      _scanning--;
      RaiseStateChanged();
    }
  }

  private void AddFile(string path)
  {
    if (_byPath.TryGetValue(path, out var existing))
    {
      // Dropping a finished file again runs it again, like ImageOptim does.
      if (existing.IsFinished)
        Enqueue(existing);
      return;
    }

    var item = new FileItem(path);
    _byPath[path] = item;
    Files.Add(item);
    Enqueue(item);
    _found++;
  }

  public void Remove(IEnumerable<FileItem> items)
  {
    foreach (var item in items.ToList())
    {
      // Files still in the queue are skipped by the workers once they leave the list.
      if (item.Status == FileStatus.Working)
        continue;
      if (item.Status == FileStatus.Queued)
        _pending--;
      Files.Remove(item);
      _byPath.Remove(item.Path);
    }
    RaiseStateChanged();
  }

  public void ClearFinished() => Remove(Files.Where(f => f.IsFinished));

  private void Enqueue(FileItem item)
  {
    item.Status = FileStatus.Queued;
    item.Result = null;
    _pending++;
    _queue.Writer.TryWrite(item);
  }

  private async Task RunWorkerAsync()
  {
    var token = _shutdown.Token;
    try
    {
      await foreach (var item in _queue.Reader.ReadAllAsync(token))
      {
        // Removed from the list (or already picked up by another worker) since it was queued.
        if (item.Status != FileStatus.Queued || !_byPath.TryGetValue(item.Path, out var current) || !ReferenceEquals(current, item))
          continue;

        item.Status = FileStatus.Working;
        RaiseStateChanged();

        var settings = Settings;
        OptimizationResult result;
        var work = Task.Run(() => _engine.OptimizeAsync(item.Path, settings, token), token);
        lock (_running)
          _running.Add(work);
        try
        {
          result = await work;
        }
        catch (OperationCanceledException)
        {
          return;
        }
        catch (Exception ex)
        {
          result = new OptimizationResult(OptimizationStatus.Failed, ImageFormat.Unknown, 0, 0, ex.Message);
        }
        finally
        {
          lock (_running)
            _running.Remove(work);
        }

        item.Apply(result);
        _pending--;
        RaiseStateChanged();
      }
    }
    catch (OperationCanceledException)
    {
    }
  }

  /// <summary>
  /// Sorts by a column. Clicking the same column again reverses the order. Size and Savings start with
  /// the largest values first, File starts alphabetically.
  /// </summary>
  public void SortBy(SortColumn column)
  {
    SortDescending = SortedBy == column ? !SortDescending : column != SortColumn.File;
    SortedBy = column;

    var property = column switch
    {
      SortColumn.Size => nameof(FileItem.SizeSortKey),
      SortColumn.Savings => nameof(FileItem.SavingsSortKey),
      _ => nameof(FileItem.Name),
    };
    var direction = SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;

    using (FilesView.DeferRefresh())
    {
      FilesView.SortDescriptions.Clear();
      FilesView.SortDescriptions.Add(new SortDescription(property, direction));
      if (column != SortColumn.File)
        FilesView.SortDescriptions.Add(new SortDescription(nameof(FileItem.Name), ListSortDirection.Ascending));
      FilesView.LiveSortingProperties.Clear();
      FilesView.LiveSortingProperties.Add(property);
    }

    OnPropertyChanged(nameof(FileHeader));
    OnPropertyChanged(nameof(SizeHeader));
    OnPropertyChanged(nameof(SavingsHeader));
    OnPropertyChanged(nameof(FileHeaderName));
    OnPropertyChanged(nameof(SizeHeaderName));
    OnPropertyChanged(nameof(SavingsHeaderName));
  }

  private string Header(string title, SortColumn column) =>
    SortedBy == column ? $"{title} {(SortDescending ? "\u25BE" : "\u25B4")}" : title;

  private string HeaderName(string title, SortColumn column) =>
    SortedBy != column ? title : SortDescending ? Strings.SortedDescending(title) : Strings.SortedAscending(title);

  private void RaiseStateChanged()
  {
    OnPropertyChanged(nameof(IsEmpty));
    OnPropertyChanged(nameof(IsBusy));
    OnPropertyChanged(nameof(IsScanning));
    OnPropertyChanged(nameof(Summary));
  }

  private void OnPropertyChanged([CallerMemberName] string? name = null) =>
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

  /// <summary>
  /// Stops the queue. Files being optimized are cut short: their tools are stopped and their temporary files
  /// removed (the originals are only replaced in a single final step, so they're left as they were).
  /// </summary>
  public void Dispose()
  {
    _shutdown.Cancel();
    _queue.Writer.TryComplete();

    // Give the cancelled files a moment to stop their tools and clean up before the app exits. The engine
    // doesn't need this thread to finish, so waiting here can't deadlock.
    Task[] running;
    lock (_running)
      running = [.. _running];
    try
    {
      Task.WaitAll(running, TimeSpan.FromSeconds(5));
    }
    catch (AggregateException)
    {
    }
  }
}

public enum SortColumn
{
  File,
  Size,
  Savings,
}
