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
    private async void SearchLibrary_Click(
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

        StopDrawing();

        const int pageSize = 20;

        var offset = 0;
        var appliedSearch = "";
        var busy = false;
        var hasNextPage = false;

        ImageItem[] results = Array.Empty<ImageItem>();
        ImageItem? chosenImage = null;

        var searchBox = new TextBox
        {
            Watermark = "Search filename or collection"
        };

        var searchButton = new Button
        {
            Content = "Search"
        };

        var previousButton = new Button
        {
            Content = "Previous",
            IsEnabled = false
        };

        var nextButton = new Button
        {
            Content = "Next",
            IsEnabled = false
        };

        var openButton = new Button
        {
            Content = "Open selected",
            IsEnabled = false
        };

        var resultsList = new ListBox
        {
            Height = 300
        };

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };

        buttons.Children.Add(searchButton);
        buttons.Children.Add(previousButton);
        buttons.Children.Add(nextButton);
        buttons.Children.Add(openButton);

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Search image library",
            FontSize = 20
        });

        panel.Children.Add(searchBox);
        panel.Children.Add(buttons);
        panel.Children.Add(resultsList);
        panel.Children.Add(status);

        var dialog = new Window
        {
            Title = "Search library",
            Width = 760,
            Height = 550,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Content = panel
        };

        void UpdateButtons()
        {
            searchBox.IsEnabled = !busy;
            searchButton.IsEnabled = !busy;

            previousButton.IsEnabled =
                !busy && offset > 0;

            nextButton.IsEnabled =
                !busy && hasNextPage;

            resultsList.IsEnabled = !busy;

            openButton.IsEnabled =
                !busy && resultsList.SelectedIndex >= 0;
        }

        async Task LoadPageAsync(
            int requestedOffset,
            string search)
        {
            if (busy)
                return;

            busy = true;
            UpdateButtons();

            try
            {
                status.Text = "Searching through GraphQL...";

                const string query = """
                    query SearchImages(
                      $search: String,
                      $skip: Int!,
                      $take: Int!
                    ) {
                      images(
                        search: $search,
                        skip: $skip,
                        take: $take
                      ) {
                        id
                        originalFileName
                        width
                        height
                      }
                    }
                    """;

                var request = new
                {
                    query,
                    variables = new
                    {
                        search,
                        skip = requestedOffset,

                        // Fetch one extra record to determine
                        // whether another page exists.
                        take = pageSize + 1
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

                if (root.TryGetProperty(
                        "errors", out var errors) &&
                    errors.ValueKind == JsonValueKind.Array &&
                    errors.GetArrayLength() > 0)
                {
                    throw new InvalidOperationException(
                        errors[0]
                            .GetProperty("message")
                            .GetString()
                        ?? "GraphQL search failed.");
                }

                if (!root.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Object ||
                    !data.TryGetProperty(
                        "images", out var imageResults) ||
                    imageResults.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException(
                        "The server returned an invalid search result.");
                }

                var received =
                    imageResults.Deserialize<ImageItem[]>(
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        })
                    ?? Array.Empty<ImageItem>();

                hasNextPage = received.Length > pageSize;
                results = received.Take(pageSize).ToArray();

                offset = requestedOffset;
                appliedSearch = search;

                resultsList.ItemsSource = results
                    .Select(image =>
                        $"{image.OriginalFileName} — " +
                        $"{image.Width} × {image.Height}")
                    .ToArray();

                resultsList.SelectedIndex =
                    results.Length > 0 ? 0 : -1;

                status.Text = results.Length == 0
                    ? "No matching images."
                    : $"Page {offset / pageSize + 1}: " +
                      $"showing results {offset + 1}–" +
                      $"{offset + results.Length}.";
            }
            catch (Exception ex)
            {
                status.Text = $"Search failed: {ex.Message}";
            }
            finally
            {
                busy = false;
                UpdateButtons();
            }
        }

        searchButton.Click += async (_, _) =>
            await LoadPageAsync(
                0, searchBox.Text?.Trim() ?? "");

        previousButton.Click += async (_, _) =>
            await LoadPageAsync(
                Math.Max(0, offset - pageSize),
                appliedSearch);

        nextButton.Click += async (_, _) =>
            await LoadPageAsync(
                offset + pageSize,
                appliedSearch);

        resultsList.SelectionChanged += (_, _) =>
            UpdateButtons();

        openButton.Click += (_, _) =>
        {
            var index = resultsList.SelectedIndex;

            if (index < 0 || index >= results.Length)
                return;

            chosenImage = results[index];
            dialog.Close();
        };

        // Keep the window alive until its current request finishes.
        dialog.Closing += (_, args) =>
        {
            if (busy)
                args.Cancel = true;
        };

        dialog.Opened += async (_, _) =>
            await LoadPageAsync(0, "");

        await dialog.ShowDialog(this);

        if (chosenImage is null)
            return;

        var existing = _library.Images.FirstOrDefault(
            image => image.Id == chosenImage.Id);

        if (existing is null)
        {
            _library.Images.Insert(0, chosenImage);
            existing = chosenImage;
        }

        _library.SelectedImage = existing;

        await _library.OpenSelectedAsync();
    }
}