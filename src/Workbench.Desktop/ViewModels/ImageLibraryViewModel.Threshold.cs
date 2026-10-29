using System;
using System.Threading.Tasks;

namespace Workbench.Desktop.ViewModels;

public partial class ImageLibraryViewModel
{
    private double _threshold = 128;

    public double Threshold
    {
        get => _threshold;
        set
        {
            _threshold = Math.Clamp(Math.Round(value), 0, 255);
            OnPropertyChanged();
        }
    }

    public async Task ApplyThresholdAsync()
    {
        var cutoff = (int)Threshold;

        await ProcessSelectedAsync(
            $"threshold?cutoff={cutoff}",
            $"Threshold ({cutoff})");
    }
}