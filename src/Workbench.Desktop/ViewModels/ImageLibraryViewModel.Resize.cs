using System;
using System.Threading.Tasks;

namespace Workbench.Desktop.ViewModels;

public partial class ImageLibraryViewModel
{
    private decimal? _resizeWidth = 300;

    public decimal? ResizeWidth
    {
        get => _resizeWidth;
        set
        {
            _resizeWidth = value;
            OnPropertyChanged();
        }
    }

    public async Task ApplyResizeAsync()
    {
        if (ResizeWidth is not decimal width ||
            width < 1 ||
            width > 8000 ||
            width != decimal.Truncate(width))
        {
            Status = "Enter a whole-number width from 1 to 8000.";
            return;
        }

        var targetWidth = (int)width;

        await ProcessSelectedAsync(
            $"resize?width={targetWidth}",
            $"Resize to {targetWidth} pixels wide");
    }
}