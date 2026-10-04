using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using ImageOptimizer.Core;
using Microsoft.Win32;

namespace ImageOptimizer.App;

public partial class MainWindow : Window
{
  private readonly MainViewModel _viewModel;
  private readonly UpdateChecker _updates;
  private bool _announcedBusy;

  public MainWindow(MainViewModel viewModel, UpdateChecker updates)
  {
    InitializeComponent();
    _viewModel = viewModel;
    _updates = updates;
    DataContext = viewModel;
    SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
    Closed += (_, _) => _viewModel.Dispose();
    _viewModel.PropertyChanged += OnViewModelPropertyChanged;
  }

  /// <summary>
  /// Tells screen readers when a batch starts and when it finishes with its summary. The per-file
  /// "Optimizing 3 of 10" updates stay readable in the footer but aren't announced, so they don't
  /// talk over each other.
  /// </summary>
  private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName != nameof(MainViewModel.Summary) || _viewModel.Summary.Length == 0)
      return;
    if (_viewModel.IsBusy && _announcedBusy)
      return;
    _announcedBusy = _viewModel.IsBusy;
    var peer = UIElementAutomationPeer.FromElement(SummaryText) ?? UIElementAutomationPeer.CreatePeerForElement(SummaryText);
    peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
  }

  public void BringToFront()
  {
    if (WindowState == WindowState.Minimized)
      WindowState = WindowState.Normal;
    Activate();
  }

  private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

  private void OnDragEnter(object sender, DragEventArgs e)
  {
    e.Effects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
    DropHighlight.Visibility = HasFiles(e) ? Visibility.Visible : Visibility.Collapsed;
    e.Handled = true;
  }

  private void OnDragOver(object sender, DragEventArgs e)
  {
    e.Effects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
    e.Handled = true;
  }

  private void OnDragLeave(object sender, DragEventArgs e) => DropHighlight.Visibility = Visibility.Collapsed;

  private void OnDrop(object sender, DragEventArgs e)
  {
    DropHighlight.Visibility = Visibility.Collapsed;
    if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
      _viewModel.Add(paths);
    e.Handled = true;
  }

  private void OnAddFiles(object sender, RoutedEventArgs e)
  {
    var patterns = string.Join(";", ImageFormats.SupportedExtensions.Select(x => "*" + x));
    var dialog = new OpenFileDialog
    {
      Title = "Add images",
      Multiselect = true,
      Filter = $"Images ({patterns})|{patterns}",
    };
    if (dialog.ShowDialog(this) == true)
      _viewModel.Add(dialog.FileNames);
  }

  private void OnSortClick(object sender, RoutedEventArgs e)
  {
    if (sender is FrameworkElement { Tag: SortColumn column })
      _viewModel.SortBy(column);
  }

  private void OnRestartToUpdate(object sender, RoutedEventArgs e)
  {
    if (!_viewModel.IsBusy)
      _updates.RestartToUpdate();
  }

  private void OnOpenSettings(object sender, RoutedEventArgs e)
  {
    var window = new SettingsWindow(_viewModel.Settings) { Owner = this };
    if (window.ShowDialog() != true)
      return;

    _viewModel.Settings = window.Settings;
    try
    {
      window.Settings.Save(OptimizerSettings.DefaultPath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      MessageBox.Show(this, $"Your settings couldn't be saved:\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void OnRemoveSelected(object sender, RoutedEventArgs e) =>
      _viewModel.Remove(FileList.SelectedItems.Cast<FileItem>());

  private void OnClearFinished(object sender, RoutedEventArgs e) => _viewModel.ClearFinished();

  private void OnShowInFolder(object sender, RoutedEventArgs e)
  {
    if (FileList.SelectedItem is FileItem item)
      RevealInExplorer(item.Path);
  }

  private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
  {
    if (FileList.SelectedItem is FileItem item && e.OriginalSource is FrameworkElement { DataContext: FileItem })
      RevealInExplorer(item.Path);
  }

  private static void RevealInExplorer(string path)
  {
    try
    {
      Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
    {
    }
  }

  private void OnPreviewKeyDown(object sender, KeyEventArgs e)
  {
    var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
    switch (e.Key)
    {
      case Key.Delete when FileList.IsKeyboardFocusWithin:
        _viewModel.Remove(FileList.SelectedItems.Cast<FileItem>());
        e.Handled = true;
        break;
      case Key.Enter when FileList.IsKeyboardFocusWithin && FileList.SelectedItem is FileItem item:
        RevealInExplorer(item.Path);
        e.Handled = true;
        break;
      case Key.O when ctrl:
        OnAddFiles(sender, e);
        e.Handled = true;
        break;
      case Key.OemComma when ctrl:
        OnOpenSettings(sender, e);
        e.Handled = true;
        break;
    }
  }
}
