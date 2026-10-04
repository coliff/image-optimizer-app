using System.Collections.ObjectModel;
using System.ComponentModel;
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
  private int _pending;
  private string? _updateVersion;

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

  public string FileHeader => Header("File", SortColumn.File);
  public string SizeHeader => Header("Size", SortColumn.Size);
  public string SavingsHeader => Header("Savings", SortColumn.Savings);

  public bool IsEmpty => Files.Count == 0;

  public bool IsBusy => _pending > 0;

  /// <summary>The version of a downloaded update that installs on restart, or null when there's none.</summary>
  public string? UpdateVersion
  {
    get => _updateVersion;
    set
    {
      _updateVersion = value;
      OnPropertyChanged();
      OnPropertyChanged(nameof(IsUpdateReady));
      OnPropertyChanged(nameof(CanRestartToUpdate));
    }
  }

  public bool IsUpdateReady => UpdateVersion is not null;

  /// <summary>Restarting would cut short the files being optimized, so it waits until they're done.</summary>
  public bool CanRestartToUpdate => IsUpdateReady && !IsBusy;

  public string Summary
  {
    get
    {
      if (Files.Count == 0)
        return "";
      if (IsBusy)
      {
        var done = Files.Count(f => f.IsFinished);
        return $"Optimizing {done + 1:N0} of {Files.Count:N0}…";
      }

      var measured = Files
          .Where(f => f.Status is FileStatus.Optimized or FileStatus.AlreadyOptimized && f.Result is not null)
          .Select(f => f.Result!)
          .ToList();
      var failed = Files.Count(f => f.Status == FileStatus.Failed);
      var failedText = failed == 0 ? "" : $" {failed:N0} {(failed == 1 ? "file" : "files")} couldn't be optimized.";

      var original = measured.Sum(r => r.OriginalSize);
      var saved = measured.Sum(r => r.BytesSaved);
      if (saved <= 0)
        return (measured.Count > 0 ? "Already optimized. Nothing to save." : "Nothing was optimized.") + failedText;

      var best = measured.Max(r => r.SavingsRatio);
      return $"Saved {SizeFormatter.Format(saved)} out of {SizeFormatter.Format(original)}. " +
             $"{SizeFormatter.Percent((double)saved / original)} overall (up to {SizeFormatter.Percent(best)} per file)." +
             failedText;
    }
  }

  /// <summary>Adds dropped files or folders and queues every image found in them.</summary>
  public int Add(IEnumerable<string> paths)
  {
    var added = 0;
    foreach (var path in FileCollector.Collect(paths))
    {
      if (_byPath.TryGetValue(path, out var existing))
      {
        // Dropping a finished file again runs it again, like ImageOptim does.
        if (existing.IsFinished)
          Enqueue(existing);
        continue;
      }

      var item = new FileItem(path);
      _byPath[path] = item;
      Files.Add(item);
      Enqueue(item);
      added++;
    }
    RaiseStateChanged();
    return added;
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
        try
        {
          result = await Task.Run(() => _engine.OptimizeAsync(item.Path, settings, token), token);
        }
        catch (OperationCanceledException)
        {
          return;
        }
        catch (Exception ex)
        {
          result = new OptimizationResult(OptimizationStatus.Failed, ImageFormat.Unknown, 0, 0, ex.Message);
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
  }

  private string Header(string title, SortColumn column) =>
    SortedBy == column ? $"{title} {(SortDescending ? "\u25BE" : "\u25B4")}" : title;

  private void RaiseStateChanged()
  {
    OnPropertyChanged(nameof(IsEmpty));
    OnPropertyChanged(nameof(IsBusy));
    OnPropertyChanged(nameof(CanRestartToUpdate));
    OnPropertyChanged(nameof(Summary));
  }

  private void OnPropertyChanged([CallerMemberName] string? name = null) =>
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

  public void Dispose()
  {
    _shutdown.Cancel();
    _queue.Writer.TryComplete();
    _shutdown.Dispose();
  }
}

public enum SortColumn
{
  File,
  Size,
  Savings,
}
