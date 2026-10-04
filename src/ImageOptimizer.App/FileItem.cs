using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageOptimizer.Core;

namespace ImageOptimizer.App;

public enum FileStatus
{
  Queued,
  Working,
  Optimized,
  AlreadyOptimized,
  Skipped,
  Failed,
}

/// <summary>One row in the file list.</summary>
public sealed class FileItem(string path) : INotifyPropertyChanged
{
  private FileStatus _status = FileStatus.Queued;
  private OptimizationResult? _result;

  public event PropertyChangedEventHandler? PropertyChanged;

  public string Path { get; } = path;
  public string Name { get; } = System.IO.Path.GetFileName(path);

  public FileStatus Status
  {
    get => _status;
    set
    {
      if (_status == value)
        return;
      _status = value;
      OnPropertyChanged();
      OnPropertyChanged(nameof(IsFinished));
      OnPropertyChanged(nameof(ToolTip));
    }
  }

  public OptimizationResult? Result
  {
    get => _result;
    set
    {
      _result = value;
      OnPropertyChanged();
      OnPropertyChanged(nameof(SizeText));
      OnPropertyChanged(nameof(SavingsText));
      OnPropertyChanged(nameof(SizeSortKey));
      OnPropertyChanged(nameof(SavingsSortKey));
      OnPropertyChanged(nameof(ToolTip));
    }
  }

  public bool IsFinished => Status is not (FileStatus.Queued or FileStatus.Working);

  /// <summary>Current size for sorting; files that haven't been measured yet sort first.</summary>
  public long SizeSortKey => Result is { OriginalSize: > 0 } r ? r.FinalSize : -1;

  /// <summary>Savings for sorting; files without a result sort below "0%".</summary>
  public double SavingsSortKey => Result is { Status: OptimizationStatus.Optimized or OptimizationStatus.AlreadyOptimized } r ? r.SavingsRatio : -1;

  public string SizeText => Result is { OriginalSize: > 0 } r ? SizeFormatter.Format(r.FinalSize) : "";

  public string SavingsText => Result switch
  {
    { Status: OptimizationStatus.Optimized } r => SizeFormatter.Percent(r.SavingsRatio),
    { Status: OptimizationStatus.AlreadyOptimized } => "0%",
    _ => "",
  };

  public string ToolTip
  {
    get
    {
      var detail = Status switch
      {
        FileStatus.Queued => "Waiting",
        FileStatus.Working => "Optimizing…",
        FileStatus.Optimized when Result is { } r =>
            $"Saved {SizeFormatter.Format(r.BytesSaved)} ({SizeFormatter.Percent(r.SavingsRatio)}), was {SizeFormatter.Format(r.OriginalSize)}",
        FileStatus.AlreadyOptimized => "Already optimized. The file was left untouched.",
        _ => (Result?.Message?.TrimEnd('.') ?? "Left untouched") + (Status == FileStatus.Failed ? ". The file was left untouched." : ""),
      };
      return $"{Path}\n{detail}";
    }
  }

  public void Apply(OptimizationResult result)
  {
    Result = result;
    Status = result.Status switch
    {
      OptimizationStatus.Optimized => FileStatus.Optimized,
      OptimizationStatus.AlreadyOptimized => FileStatus.AlreadyOptimized,
      OptimizationStatus.Skipped => FileStatus.Skipped,
      _ => FileStatus.Failed,
    };
  }

  private void OnPropertyChanged([CallerMemberName] string? name = null) =>
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
