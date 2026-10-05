using ImageOptimizer.Core;

namespace ImageOptimizer.App.Tests;

public class SortingTests
{
  private static FileItem Item(string name, long original, long final, OptimizationStatus status = OptimizationStatus.Optimized)
  {
    var item = new FileItem($@"C:\images\{name}");
    item.Apply(new OptimizationResult(status, ImageFormat.Png, original, final));
    return item;
  }

  private static List<string> Order(MainViewModel viewModel) =>
    viewModel.FilesView.Cast<FileItem>().Select(f => f.Name).ToList();

  [Fact]
  public void Column_headers_sort_and_reverse()
  {
    using var viewModel = new MainViewModel(ImageOptimizerEngine.CreateDefault(new ToolRunner()), new OptimizerSettings());
    viewModel.Files.Add(Item("b.png", 1000, 500));   // 50% saved, 500 bytes
    viewModel.Files.Add(Item("a.png", 4000, 3600));  // 10% saved, 3600 bytes
    viewModel.Files.Add(Item("c.png", 300, 300, OptimizationStatus.AlreadyOptimized)); // 0% saved, 300 bytes

    viewModel.SortBy(SortColumn.Savings);
    Assert.Equal(["b.png", "a.png", "c.png"], Order(viewModel));
    Assert.EndsWith("\u25BE", viewModel.SavingsHeader);
    Assert.Equal("Size", viewModel.SizeHeader);
    Assert.Equal("Savings, sorted descending", viewModel.SavingsHeaderName);
    Assert.Equal("Size", viewModel.SizeHeaderName);

    viewModel.SortBy(SortColumn.Savings);
    Assert.Equal(["c.png", "a.png", "b.png"], Order(viewModel));

    viewModel.SortBy(SortColumn.Size);
    Assert.Equal(["a.png", "b.png", "c.png"], Order(viewModel));

    viewModel.SortBy(SortColumn.File);
    Assert.Equal(["a.png", "b.png", "c.png"], Order(viewModel));
    Assert.EndsWith("\u25B4", viewModel.FileHeader);
    Assert.Equal("File, sorted ascending", viewModel.FileHeaderName);
  }
}
