using System;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Workbench.Desktop.Views;

public partial class MainWindow
{
    private bool _drawingEnabled;
    private bool _drawingEventsConnected;
    private bool _savingDrawing;
    private bool _preparingDrawing;

    private Point _drawingStart;
    private Rectangle? _draftRectangle;

    private Guid _drawingImageId;
    private int _drawingImageWidth;
    private int _drawingImageHeight;

    private object? _drawingPreview;

    private async void DrawAnnotation_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_library is null ||
            _library.IsBusy ||
            _savingDrawing ||
            _preparingDrawing ||
            _loadingAnnotations)
        {
            return;
        }

        var selected = _library.SelectedImage;

        if (selected is null)
        {
            await ShowAnnotationMessageAsync(
                "Select an image first.");
            return;
        }

        _preparingDrawing = true;

        try
        {
            StopDrawing();

            var previousPreview = _library.Preview;

            await _library.OpenSelectedAsync();

            if (_library.SelectedImage?.Id != selected.Id ||
                _library.Preview is null ||
                ReferenceEquals(
                    _library.Preview, previousPreview))
            {
                return;
            }

            var canvas =
                this.FindControl<Canvas>("AnnotationCanvas");

            if (canvas is null)
                return;

            if (!_drawingEventsConnected)
            {
                canvas.PointerPressed += Drawing_PointerPressed;
                canvas.PointerMoved += Drawing_PointerMoved;
                canvas.PointerReleased += Drawing_PointerReleased;

                canvas.PointerCaptureLost += (_, _) =>
                {
                    if (_drawingEnabled)
                        StopDrawing();
                };

                _library.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == "Preview" ||
                        args.PropertyName == "SelectedImage")
                    {
                        StopDrawing();
                    }
                };

                _drawingEventsConnected = true;
            }

            ClearAnnotationOverlay();

            _drawingImageId = selected.Id;
            _drawingImageWidth = selected.Width;
            _drawingImageHeight = selected.Height;
            _drawingPreview = _library.Preview;

            _drawingEnabled = true;

            canvas.Background = Brushes.Transparent;
            canvas.IsHitTestVisible = true;
            canvas.Cursor =
                new Cursor(StandardCursorType.Cross);
        }
        finally
        {
            _preparingDrawing = false;
        }
    }

    private bool DrawingImageIsCurrent()
    {
        return _library is not null &&
               _library.SelectedImage?.Id == _drawingImageId &&
               ReferenceEquals(
                   _library.Preview, _drawingPreview);
    }

    private static Point ClampDrawingPoint(
        Point point,
        Canvas canvas)
    {
        return new Point(
            Math.Clamp(point.X, 0, canvas.Bounds.Width),
            Math.Clamp(point.Y, 0, canvas.Bounds.Height));
    }

    private void Drawing_PointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (!_drawingEnabled ||
            _draftRectangle is not null ||
            sender is not Canvas canvas ||
            !DrawingImageIsCurrent() ||
            !e.GetCurrentPoint(canvas)
                .Properties.IsLeftButtonPressed)
        {
            return;
        }

        _drawingStart = ClampDrawingPoint(
            e.GetPosition(canvas), canvas);

        _draftRectangle = new Rectangle
        {
            Stroke = Brushes.Yellow,
            StrokeThickness = 3,
            Fill = new SolidColorBrush(
                Color.FromArgb(40, 255, 255, 0))
        };

        Canvas.SetLeft(_draftRectangle, _drawingStart.X);
        Canvas.SetTop(_draftRectangle, _drawingStart.Y);

        canvas.Children.Add(_draftRectangle);

        e.Pointer.Capture(canvas);
        e.Handled = true;
    }

    private void UpdateDraft(Canvas canvas, Point position)
    {
        if (_draftRectangle is null)
            return;

        var point = ClampDrawingPoint(position, canvas);

        Canvas.SetLeft(
            _draftRectangle,
            Math.Min(_drawingStart.X, point.X));

        Canvas.SetTop(
            _draftRectangle,
            Math.Min(_drawingStart.Y, point.Y));

        _draftRectangle.Width =
            Math.Abs(point.X - _drawingStart.X);

        _draftRectangle.Height =
            Math.Abs(point.Y - _drawingStart.Y);
    }

    private void Drawing_PointerMoved(
        object? sender,
        PointerEventArgs e)
    {
        if (!_drawingEnabled ||
            _draftRectangle is null ||
            sender is not Canvas canvas)
        {
            return;
        }

        UpdateDraft(canvas, e.GetPosition(canvas));
        e.Handled = true;
    }

    private async void Drawing_PointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (!_drawingEnabled ||
            _draftRectangle is null ||
            sender is not Canvas canvas)
        {
            return;
        }

        UpdateDraft(canvas, e.GetPosition(canvas));

        var rectangle = _draftRectangle;
        var preview = _library?.Preview;

        _drawingEnabled = false;
        canvas.IsHitTestVisible = false;
        canvas.Cursor = Cursor.Default;

        e.Pointer.Capture(null);
        e.Handled = true;

        if (preview is null ||
            !DrawingImageIsCurrent() ||
            rectangle.Width < 1 ||
            rectangle.Height < 1 ||
            preview.Size.Width <= 0 ||
            preview.Size.Height <= 0)
        {
            StopDrawing();
            return;
        }

        var scaleX =
            _drawingImageWidth / preview.Size.Width;

        var scaleY =
            _drawingImageHeight / preview.Size.Height;

        var x = Math.Clamp(
            Canvas.GetLeft(rectangle) * scaleX,
            0, _drawingImageWidth);

        var y = Math.Clamp(
            Canvas.GetTop(rectangle) * scaleY,
            0, _drawingImageHeight);

        var width = Math.Min(
            rectangle.Width * scaleX,
            _drawingImageWidth - x);

        var height = Math.Min(
            rectangle.Height * scaleY,
            _drawingImageHeight - y);

        if (width <= 0 || height <= 0)
        {
            StopDrawing();
            return;
        }

        var imageId = _drawingImageId;

        _savingDrawing = true;

        try
        {
            var details = await AskAnnotationDetailsAsync();

            if (details is null || !DrawingImageIsCurrent())
                return;

            await SaveAnnotationThroughGraphQLAsync(
                imageId,
                details.Value.Label,
                details.Value.Notes,
                x, y, width, height);

            await ShowAnnotationMessageAsync(
                "Annotation saved through GraphQL. " +
                "Click Show annotations to view it.");
        }
        catch (Exception ex)
        {
            await ShowAnnotationMessageAsync(
                $"Could not confirm annotation save: {ex.Message}");
        }
        finally
        {
            _savingDrawing = false;
            StopDrawing();
        }
    }

    private static async Task SaveAnnotationThroughGraphQLAsync(
        Guid imageId,
        string label,
        string notes,
        double x,
        double y,
        double width,
        double height)
    {
        const string mutation = """
            mutation SaveAnnotation(
              $imageId: UUID!,
              $label: String!,
              $notes: String!,
              $x: Float!,
              $y: Float!,
              $width: Float!,
              $height: Float!
            ) {
              addAnnotation(
                imageId: $imageId,
                input: {
                  label: $label,
                  notes: $notes,
                  x: $x,
                  y: $y,
                  width: $width,
                  height: $height
                }
              ) {
                id
                label
              }
            }
            """;

        var request = new
        {
            query = mutation,
            variables = new
            {
                imageId,
                label,
                notes,
                x,
                y,
                width,
                height
            }
        };

        using var response =
            await AnnotationClient.PostAsJsonAsync(
                "graphql", request);

        response.EnsureSuccessStatusCode();

        using var stream =
            await response.Content.ReadAsStreamAsync();

        using var document =
            await JsonDocument.ParseAsync(stream);

        var root = document.RootElement;

        // GraphQL errors can arrive with HTTP status 200.
        if (root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            var message = errors[0]
                .GetProperty("message")
                .GetString();

            throw new InvalidOperationException(
                message ?? "GraphQL mutation failed.");
        }

        if (!root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty(
                "addAnnotation", out var annotation) ||
            annotation.ValueKind != JsonValueKind.Object ||
            !annotation.TryGetProperty("id", out var id) ||
            id.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(id.GetString(), out _))
        {
            throw new InvalidOperationException(
                "The server did not return an annotation ID.");
        }
    }

    private void StopDrawing()
    {
        _drawingEnabled = false;

        var canvas =
            this.FindControl<Canvas>("AnnotationCanvas");

        if (canvas is not null)
        {
            canvas.IsHitTestVisible = false;
            canvas.Cursor = Cursor.Default;

            if (_draftRectangle is not null)
                canvas.Children.Remove(_draftRectangle);
        }

        _draftRectangle = null;
    }

    private async Task<(string Label, string Notes)?>
        AskAnnotationDetailsAsync()
    {
        var labelBox = new TextBox
        {
            Watermark = "Example: Region of interest",
            MaxLength = 200
        };

        var notesBox = new TextBox
        {
            Watermark = "Optional notes",
            AcceptsReturn = true,
            Height = 90,
            MaxLength = 4000
        };

        var validation = new TextBlock
        {
            Foreground = Brushes.Red
        };

        var saveButton = new Button { Content = "Save" };
        var cancelButton = new Button { Content = "Cancel" };

        var buttons = new StackPanel
        {
            Orientation =
                Avalonia.Layout.Orientation.Horizontal,
            Spacing = 12
        };

        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10
        };

        panel.Children.Add(new TextBlock { Text = "Label" });
        panel.Children.Add(labelBox);
        panel.Children.Add(new TextBlock { Text = "Notes" });
        panel.Children.Add(notesBox);
        panel.Children.Add(validation);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Save annotation",
            Width = 440,
            Height = 360,
            CanResize = false,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Content = panel
        };

        saveButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(labelBox.Text))
            {
                validation.Text = "Enter a label.";
                return;
            }

            dialog.Close(true);
        };

        cancelButton.Click += (_, _) =>
            dialog.Close(false);

        var saved = await dialog.ShowDialog<bool>(this);

        if (!saved)
            return null;

        return (
            labelBox.Text!.Trim(),
            notesBox.Text?.Trim() ?? "");
    }
}