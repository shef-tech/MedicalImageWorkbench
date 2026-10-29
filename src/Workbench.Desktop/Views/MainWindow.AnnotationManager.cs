using System;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Workbench.Desktop.Views;

public partial class MainWindow
{
    private async void ManageAnnotations_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_library is null ||
            _library.IsBusy ||
            _loadingAnnotations ||
            _savingDrawing ||
            _preparingDrawing)
        {
            return;
        }

        var image = _library.SelectedImage;

        if (image is null)
        {
            await ShowAnnotationMessageAsync(
                "Select an image first.");
            return;
        }

        StopDrawing();

        AnnotationItem[] annotations =
            Array.Empty<AnnotationItem>();

        var list = new ListBox
        {
            Height = 180
        };

        var labelBox = new TextBox
        {
            MaxLength = 200,
            Watermark = "Annotation label"
        };

        var notesBox = new TextBox
        {
            MaxLength = 4000,
            AcceptsReturn = true,
            Height = 90,
            Watermark = "Notes"
        };

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        var saveButton = new Button
        {
            Content = "Save changes"
        };

        var deleteButton = new Button
        {
            Content = "Delete selected"
        };

        var closeButton = new Button
        {
            Content = "Close"
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };

        buttons.Children.Add(saveButton);
        buttons.Children.Add(deleteButton);
        buttons.Children.Add(closeButton);

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"Annotations: {image.OriginalFileName}",
            FontSize = 18
        });

        panel.Children.Add(list);
        panel.Children.Add(new TextBlock
        {
            Text = "Label"
        });
        panel.Children.Add(labelBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Notes"
        });
        panel.Children.Add(notesBox);
        panel.Children.Add(status);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Manage annotations",
            Width = 600,
            Height = 620,
            CanResize = false,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Content = panel
        };

        AnnotationItem? GetSelected()
        {
            var index = list.SelectedIndex;

            return index >= 0 && index < annotations.Length
                ? annotations[index]
                : null;
        }

        void SetBusy(bool busy)
        {
            list.IsEnabled = !busy;
            labelBox.IsEnabled = !busy;
            notesBox.IsEnabled = !busy;
            saveButton.IsEnabled = !busy;
            deleteButton.IsEnabled = !busy;
            closeButton.IsEnabled = !busy;
        }

        async Task ReloadAsync(Guid? selectedId = null)
        {
            annotations =
                await AnnotationClient
                    .GetFromJsonAsync<AnnotationItem[]>(
                        $"images/{image.Id}/annotations")
                ?? Array.Empty<AnnotationItem>();

            list.ItemsSource = annotations
                .Select(annotation => annotation.Label)
                .ToArray();

            labelBox.Text = "";
            notesBox.Text = "";

            var index = selectedId.HasValue
                ? Array.FindIndex(
                    annotations,
                    annotation =>
                        annotation.Id == selectedId.Value)
                : -1;

            list.SelectedIndex = index >= 0
                ? index
                : annotations.Length > 0 ? 0 : -1;

            status.Text =
                $"{annotations.Length} annotation(s).";
        }

        list.SelectionChanged += (_, _) =>
        {
            var selected = GetSelected();

            labelBox.Text = selected?.Label ?? "";
            notesBox.Text = selected?.Notes ?? "";
        };

        saveButton.Click += async (_, _) =>
        {
            var selected = GetSelected();

            if (selected is null)
            {
                status.Text = "Select an annotation first.";
                return;
            }

            var label = labelBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(label))
            {
                status.Text = "Enter a label.";
                return;
            }

            SetBusy(true);

            try
            {
                var input = new
                {
                    Label = label,
                    Notes = notesBox.Text?.Trim() ?? "",
                    selected.X,
                    selected.Y,
                    selected.Width,
                    selected.Height
                };

                using var response =
                    await AnnotationClient.PutAsJsonAsync(
                        $"images/{image.Id}/annotations/{selected.Id}",
                        input);

                if (!response.IsSuccessStatusCode)
                {
                    status.Text =
                        await response.Content
                            .ReadAsStringAsync();

                    return;
                }

                await ReloadAsync(selected.Id);

                status.Text = "Changes saved.";
            }
            catch (Exception)
            {
                status.Text =
                    "Could not save. Check the API connection.";
            }
            finally
            {
                SetBusy(false);
            }
        };

        deleteButton.Click += async (_, _) =>
        {
            var selected = GetSelected();

            if (selected is null)
            {
                status.Text = "Select an annotation first.";
                return;
            }

            var confirmButton = new Button
            {
                Content = "Delete"
            };

            var cancelButton = new Button
            {
                Content = "Cancel"
            };

            var confirmationButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12
            };

            confirmationButtons.Children.Add(confirmButton);
            confirmationButtons.Children.Add(cancelButton);

            var confirmationPanel = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16
            };

            confirmationPanel.Children.Add(new TextBlock
            {
                Text =
                    $"Delete annotation \"{selected.Label}\"?",
                TextWrapping = TextWrapping.Wrap
            });

            confirmationPanel.Children.Add(
                confirmationButtons);

            var confirmation = new Window
            {
                Title = "Delete annotation",
                Width = 420,
                Height = 200,
                CanResize = false,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                Content = confirmationPanel
            };

            confirmButton.Click += (_, _) =>
                confirmation.Close(true);

            cancelButton.Click += (_, _) =>
                confirmation.Close(false);

            var confirmed =
                await confirmation.ShowDialog<bool>(dialog);

            if (!confirmed)
                return;

            SetBusy(true);

            try
            {
                using var response =
                    await AnnotationClient.DeleteAsync(
                        $"images/{image.Id}/annotations/{selected.Id}");

                if (!response.IsSuccessStatusCode)
                {
                    status.Text =
                        await response.Content
                            .ReadAsStringAsync();

                    return;
                }

                await ReloadAsync();

                status.Text = "Annotation deleted.";
            }
            catch (Exception)
            {
                status.Text =
                    "Could not delete. Check the API connection.";
            }
            finally
            {
                SetBusy(false);
            }
        };

        closeButton.Click += (_, _) =>
            dialog.Close();

        dialog.Opened += async (_, _) =>
        {
            SetBusy(true);

            try
            {
                await ReloadAsync();
            }
            catch (Exception)
            {
                status.Text =
                    "Could not load annotations. Check the API.";
            }
            finally
            {
                SetBusy(false);
            }
        };

        await dialog.ShowDialog(this);

        // Remove rectangles whose labels or records may have changed.
        ClearAnnotationOverlay();
    }
}