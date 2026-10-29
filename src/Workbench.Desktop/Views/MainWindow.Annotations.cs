using System;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

public class AnnotationItem
{
    public Guid Id { get; set; }
    public string Label { get; set; } = "";
    public string Notes { get; set; } = "";

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public partial class MainWindow
{
    private static readonly HttpClient AnnotationClient = new()
    {
        BaseAddress = new Uri("http://localhost:5050/")
    };

    private bool _annotationEventsConnected;
    private bool _loadingAnnotations;

    private void ClearAnnotationOverlay()
    {
        var canvas =
            this.FindControl<Canvas>("AnnotationCanvas");

        canvas?.Children.Clear();
    }

    private void AnnotationPreviewChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageLibraryViewModel.Preview) ||
            e.PropertyName == nameof(ImageLibraryViewModel.SelectedImage))
        {
            ClearAnnotationOverlay();
        }
    }

    private async void ShowAnnotations_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_library is null ||
            _library.IsBusy ||
            _loadingAnnotations)
        {
            return;
        }

        var selected = _library.SelectedImage;

        if (selected is null)
            return;

        var canvas =
            this.FindControl<Canvas>("AnnotationCanvas");

        if (canvas is null)
            return;

        if (!_annotationEventsConnected)
        {
            _library.PropertyChanged += AnnotationPreviewChanged;

            Closed += (_, _) =>
            {
                if (_library is not null)
                {
                    _library.PropertyChanged -=
                        AnnotationPreviewChanged;
                }
            };

            _annotationEventsConnected = true;
        }

        var button = sender as Button;

        _loadingAnnotations = true;

        if (button is not null)
            button.IsEnabled = false;

        try
        {
            ClearAnnotationOverlay();

            // Display the original before drawing its annotations.
            await _library.OpenSelectedAsync();

            if (_library.SelectedImage?.Id != selected.Id)
                return;

            var preview = _library.Preview;

            if (preview is null ||
                selected.Width <= 0 ||
                selected.Height <= 0)
            {
                return;
            }

            var annotations =
                await AnnotationClient.GetFromJsonAsync<AnnotationItem[]>(
                    $"images/{selected.Id}/annotations");

            // Discard results if another image or preview was opened.
            if (_library.SelectedImage?.Id != selected.Id ||
                !ReferenceEquals(_library.Preview, preview))
            {
                return;
            }

            ClearAnnotationOverlay();

            var scaleX = preview.Size.Width / selected.Width;
            var scaleY = preview.Size.Height / selected.Height;

            foreach (var annotation in
                annotations ?? Array.Empty<AnnotationItem>())
            {
                var rectangle = new Rectangle
                {
                    Width = annotation.Width * scaleX,
                    Height = annotation.Height * scaleY,
                    Stroke = Brushes.Lime,
                    StrokeThickness = 3,
                    Fill = new SolidColorBrush(
                        Color.FromArgb(35, 0, 255, 0))
                };

                Canvas.SetLeft(
                    rectangle, annotation.X * scaleX);

                Canvas.SetTop(
                    rectangle, annotation.Y * scaleY);

                canvas.Children.Add(rectangle);

                var label = new TextBlock
                {
                    Text = annotation.Label,
                    Foreground = Brushes.Black,
                    Background = Brushes.Lime,
                    FontSize = 14
                };

                Canvas.SetLeft(
                    label, annotation.X * scaleX);

                Canvas.SetTop(
                    label, annotation.Y * scaleY);

                canvas.Children.Add(label);
            }

            if (annotations is null || annotations.Length == 0)
            {
                await ShowAnnotationMessageAsync(
                    "No saved annotations for this image.");
            }
        }
        catch (Exception)
        {
            ClearAnnotationOverlay();

            await ShowAnnotationMessageAsync(
                "Could not load annotations. " +
                "Check that the API is running.");
        }
        finally
        {
            _loadingAnnotations = false;

            if (button is not null)
                button.IsEnabled = true;
        }
    }

    private async System.Threading.Tasks.Task
        ShowAnnotationMessageAsync(string message)
    {
        var dialog = new Window
        {
            Title = "Annotations",
            Width = 420,
            Height = 160,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20)
            }
        };

        await dialog.ShowDialog(this);
    }
}