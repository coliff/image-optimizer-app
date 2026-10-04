using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ImageOptimizer.Core;
using Microsoft.Win32;

namespace ImageOptimizer.App;

public partial class MainWindow : Window
{
  private readonly MainViewModel _viewModel;

  public MainWindow(MainViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
    Closed += (_, _) => _viewModel.Dispose();
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

  private void OnRunAgain(object sender, RoutedEventArgs e) => _viewModel.RunAgain();

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
      case Key.O when ctrl:
        OnAddFiles(sender, e);
        e.Handled = true;
        break;
      case Key.R when ctrl && _viewModel.CanRunAgain:
        _viewModel.RunAgain();
        e.Handled = true;
        break;
      case Key.OemComma when ctrl:
        OnOpenSettings(sender, e);
        e.Handled = true;
        break;
    }
  }
}
