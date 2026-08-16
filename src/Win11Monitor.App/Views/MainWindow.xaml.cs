using Microsoft.Win32;
using Win11Monitor.App.ViewModels;

namespace Win11Monitor.App.Views;

public partial class MainWindow : System.Windows.Window
{
    private readonly MonitorViewModel _viewModel;

    public MainWindow(MonitorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void Hide_Click(object sender, System.Windows.RoutedEventArgs e) => Hide();

    private void ExportDiagnostics_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出传感器报告",
            Filter = "文本文件 (*.txt)|*.txt",
            FileName = $"z690-sensors-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _viewModel.ExportDiagnostics(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(
                this,
                $"导出失败：{ex.Message}",
                "Z690 Monitor",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}
