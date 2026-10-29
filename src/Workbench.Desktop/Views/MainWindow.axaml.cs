using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

public partial class MainWindow : Window
{
    private ImageLibraryViewModel? _library;

    public MainWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            _library = new ImageLibraryViewModel();
            _library.PropertyChanged += Library_PropertyChanged;

            DataContext = _library;
        };

        Closed += (_, _) =>
        {
            if (_library is not null)
            {
                _library.PropertyChanged -= Library_PropertyChanged;
            }
        };
    }

    private void Library_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageLibraryViewModel.Preview))
        {
            Dispatcher.UIThread.Post(
                FitImage,
                DispatcherPriority.Loaded);
        }
    }

    private void FitImage()
    {
        var preview = _library?.Preview;

        if (preview is null)
            return;

        var scroller =
            this.FindControl<ScrollViewer>("ImageScroller");

        var slider =
            this.FindControl<Slider>("ZoomSlider");

        if (scroller is null || slider is null)
            return;

        var availableWidth = scroller.Bounds.Width - 24;
        var availableHeight = scroller.Bounds.Height - 24;

        if (availableWidth <= 0 || availableHeight <= 0)
            return;

        var imageSize = preview.Size;

        if (imageSize.Width <= 0 || imageSize.Height <= 0)
            return;

        var scale = Math.Min(
            availableWidth / imageSize.Width,
            availableHeight / imageSize.Height);

        slider.Value = Math.Clamp(
            Math.Min(scale, 1),
            slider.Minimum,
            slider.Maximum);

        scroller.Offset = new Vector(0, 0);
    }

    private void Fit_Click(
        object? sender,
        RoutedEventArgs e)
    {
        FitImage();
    }

    private void ActualSize_Click(
        object? sender,
        RoutedEventArgs e)
    {
        var slider =
            this.FindControl<Slider>("ZoomSlider");

        if (slider is not null)
        {
            slider.Value = 1;
        }
    }

    private async void Import_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_library is null || _library.IsBusy)
            return;

        var button = sender as Button;

        if (button is not null)
        {
            button.IsEnabled = false;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Select a PNG or JPEG image",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("PNG and JPEG images")
                        {
                            Patterns = new[]
                            {
                                "*.png",
                                "*.jpg",
                                "*.jpeg"
                            }
                        }
                    }
                });

            if (files.Count == 0)
                return;

            using var stream = await files[0].OpenReadAsync();

            await _library.ImportImageAsync(
                stream,
                files[0].Name);
        }
        catch (Exception)
        {
            var dialog = new Window
            {
                Title = "Import error",
                Width = 420,
                Height = 140,
                Content = new TextBlock
                {
                    Text = "Could not open the selected file.",
                    Margin = new Thickness(20)
                }
            };

            await dialog.ShowDialog(this);
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }
        }
    }

    private async void Refresh_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_library is not null)
        {
            await _library.LoadImagesAsync();
        }
    }

    private async void Open_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_library is not null)
        {
            await _library.OpenSelectedAsync();
        }
    }

    private async void Grayscale_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_library is not null)
        {
            await _library.ApplyGrayscaleAsync();
        }
    }

    private async void Adjust_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_library is not null)
        {
            await _library.ApplyAdjustmentsAsync();
        }
    }

    private void Reset_Click(
        object? sender,
        RoutedEventArgs e)
    {
        _library?.ResetAdjustments();
    }
}