using System;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Workbench.Desktop.ViewModels;

namespace Workbench.Desktop.Views;

public partial class MainWindow
{
    private async Task<JsonElement> ExecuteMetadataGraphQLAsync(
        string query,
        object variables,
        string resultField)
    {
        using var response =
            await AnnotationClient.PostAsJsonAsync(
                "graphql",
                new
                {
                    query,
                    variables
                });

        response.EnsureSuccessStatusCode();

        using var stream =
            await response.Content.ReadAsStreamAsync();

        using var document =
            await JsonDocument.ParseAsync(stream);

        var root = document.RootElement;

        if (root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            throw new InvalidOperationException(
                errors[0]
                    .GetProperty("message")
                    .GetString()
                ?? "GraphQL operation failed.");
        }

        if (!root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty(resultField, out var result) ||
            result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "The server returned no image metadata.");
        }

        return result.Clone();
    }

    private async void EditMetadata_Click(
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

        var selected = _library.SelectedImage;

        if (selected is null)
        {
            await ShowAnnotationMessageAsync(
                "Select an image first.");
            return;
        }

        StopDrawing();

        var nameBox = new TextBox
        {
            MaxLength = 255,
            Watermark = "Example: flower.jpg"
        };

        var collectionBox = new TextBox
        {
            MaxLength = 200,
            Watermark = "Example: Demo Experiment"
        };

        var details = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        var saveButton = new Button
        {
            Content = "Save metadata",
            IsEnabled = false
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
        buttons.Children.Add(closeButton);

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Edit image metadata",
            FontSize = 20
        });

        panel.Children.Add(details);
        panel.Children.Add(new TextBlock
        {
            Text = "Filename"
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Collection / experiment"
        });
        panel.Children.Add(collectionBox);
        panel.Children.Add(status);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Edit metadata",
            Width = 560,
            Height = 430,
            CanResize = false,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Content = panel
        };

        var busy = false;

        void SetBusy(bool value)
        {
            busy = value;
            nameBox.IsEnabled = !value;
            collectionBox.IsEnabled = !value;
            saveButton.IsEnabled = !value;
            closeButton.IsEnabled = !value;
        }

        dialog.Closing += (_, args) =>
        {
            if (busy)
                args.Cancel = true;
        };

        dialog.Opened += async (_, _) =>
        {
            SetBusy(true);

            try
            {
                status.Text = "Loading metadata...";

                const string query = """
                    query ImageMetadata($id: UUID!) {
                      image(id: $id) {
                        id
                        originalFileName
                        collectionName
                        width
                        height
                        fileFormat
                        fileSize
                      }
                    }
                    """;

                var result =
                    await ExecuteMetadataGraphQLAsync(
                        query,
                        new { id = selected.Id },
                        "image");

                nameBox.Text = result
                    .GetProperty("originalFileName")
                    .GetString();

                collectionBox.Text = result
                    .GetProperty("collectionName")
                    .GetString();

                var width = result
                    .GetProperty("width")
                    .GetInt32();

                var height = result
                    .GetProperty("height")
                    .GetInt32();

                var format = result
                    .GetProperty("fileFormat")
                    .GetString();

                var fileSize = result
                    .GetProperty("fileSize")
                    .GetInt64();

                details.Text =
                    $"{width} × {height} pixels | " +
                    $"{format} | {fileSize:N0} bytes";

                status.Text =
                    "Changes update metadata; the stored image is preserved.";
            }
            catch (Exception ex)
            {
                status.Text =
                    $"Could not load metadata: {ex.Message}";

                // Avoid saving blank fields after a failed load.
                SetBusy(false);
                saveButton.IsEnabled = false;
                return;
            }

            SetBusy(false);
        };

        saveButton.Click += async (_, _) =>
        {
            if (busy)
                return;

            var fileName = nameBox.Text?.Trim() ?? "";
            var collection = collectionBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(fileName))
            {
                status.Text = "Enter a filename.";
                return;
            }

            SetBusy(true);

            try
            {
                status.Text = "Saving through GraphQL...";

                const string mutation = """
                    mutation UpdateMetadata(
                      $id: UUID!,
                      $fileName: String!,
                      $collection: String!
                    ) {
                      updateImageMetadata(
                        id: $id,
                        originalFileName: $fileName,
                        collectionName: $collection
                      ) {
                        id
                        originalFileName
                        collectionName
                        width
                        height
                      }
                    }
                    """;

                var result =
                    await ExecuteMetadataGraphQLAsync(
                        mutation,
                        new
                        {
                            id = selected.Id,
                            fileName,
                            collection
                        },
                        "updateImageMetadata");

                var updated = new ImageItem
                {
                    Id = result.GetProperty("id").GetGuid(),

                    OriginalFileName = result
                        .GetProperty("originalFileName")
                        .GetString() ?? "",

                    Width = result
                        .GetProperty("width")
                        .GetInt32(),

                    Height = result
                        .GetProperty("height")
                        .GetInt32()
                };

                var existing = _library.Images.FirstOrDefault(
                    image => image.Id == updated.Id);

                if (existing is not null)
                {
                    var index =
                        _library.Images.IndexOf(existing);

                    _library.Images[index] = updated;
                }
                else
                {
                    _library.Images.Insert(0, updated);
                }

                _library.SelectedImage = updated;

                status.Text = "Metadata saved successfully.";
            }
            catch (Exception ex)
            {
                status.Text =
                    $"Save was not confirmed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        };

        closeButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
    }
}