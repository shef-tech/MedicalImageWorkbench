using Avalonia.Interactivity;

namespace Workbench.Desktop.Views;

public partial class MainWindow
{
    private async void Threshold_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_library is not null)
        {
            await _library.ApplyThresholdAsync();
        }
    }
}