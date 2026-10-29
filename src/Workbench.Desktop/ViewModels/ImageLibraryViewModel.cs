using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Workbench.Desktop.ViewModels;

public class ImageItem : INotifyPropertyChanged
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }

    private Bitmap? _thumbnail;

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set
        {
            var previous = _thumbnail;
            _thumbnail = value;

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Thumbnail)));

            if (!ReferenceEquals(previous, value))
                previous?.Dispose();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ImageLibraryViewModel : INotifyPropertyChanged
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("http://localhost:5050/")
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ObservableCollection<ImageItem> Images { get; } = new();

    private ImageItem? _selectedImage;

    public ImageItem? SelectedImage
    {
        get => _selectedImage;
        set
        {
            _selectedImage = value;
            OnPropertyChanged();
        }
    }

    private Bitmap? _preview;

    public Bitmap? Preview
    {
        get => _preview;
        private set
        {
            var previous = _preview;
            _preview = value;

            OnPropertyChanged();

            previous?.Dispose();
        }
    }

    private string _status = "Click Refresh library to load images.";

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInteract));
        }
    }

    public bool CanInteract => !IsBusy;

    private double _brightness;

    public double Brightness
    {
        get => _brightness;
        set
        {
            _brightness = value;
            OnPropertyChanged();
        }
    }

    private double _contrast = 1;

    public double Contrast
    {
        get => _contrast;
        set
        {
            _contrast = value;
            OnPropertyChanged();
        }
    }

    private static async Task LoadThumbnailAsync(ImageItem image)
    {
        try
        {
            var bytes = await Client.GetByteArrayAsync(
                $"images/{image.Id}/thumbnail");

            using var stream = new MemoryStream(bytes);

            image.Thumbnail = new Bitmap(stream);
        }
        catch (Exception)
        {
            // A failed thumbnail must not prevent image browsing.
            image.Thumbnail = null;
        }
    }

    private async Task RefreshLibraryCoreAsync()
    {
        const string query = """
            query LibraryImages($take: Int!, $skip: Int!) {
              images(take: $take, skip: $skip) {
                id
                originalFileName
                width
                height
              }
            }
            """;

        using var response = await Client.PostAsJsonAsync(
            "graphql",
            new
            {
                query,
                variables = new { take = 50, skip = 0 }
            });

        response.EnsureSuccessStatusCode();

        using var responseStream =
            await response.Content.ReadAsStreamAsync();

        using var document =
            await JsonDocument.ParseAsync(responseStream);

        var root = document.RootElement;

        if (root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            throw new InvalidOperationException(
                errors[0].GetProperty("message").GetString()
                ?? "GraphQL query failed.");
        }

        if (!root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("images", out var imageResults) ||
            imageResults.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "The server returned an invalid image list.");
        }

        var images = imageResults.Deserialize<ImageItem[]>(
            JsonOptions) ?? Array.Empty<ImageItem>();

        var previousSelection = SelectedImage?.Id;

        foreach (var oldImage in Images)
            oldImage.Thumbnail = null;

        Images.Clear();

        foreach (var image in images)
            Images.Add(image);

        ImageItem? selected = null;

        foreach (var image in Images)
        {
            if (image.Id == previousSelection)
            {
                selected = image;
                break;
            }
        }

        SelectedImage = selected ??
            (Images.Count > 0 ? Images[0] : null);

        // Sequential thumbnail requests keep first-time generation
        // from decoding many large originals simultaneously.
        foreach (var image in images)
            await LoadThumbnailAsync(image);
    }

    private async Task OpenImageCoreAsync(ImageItem image)
    {
        var bytes = await Client.GetByteArrayAsync(
            $"images/{image.Id}/file");

        ShowPreview(bytes);
    }

    private void ShowPreview(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        Preview = new Bitmap(stream);
    }

    public async Task LoadImagesAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;

        try
        {
            Status = "Loading library and thumbnails...";

            await RefreshLibraryCoreAsync();

            Status =
                $"Loaded {Images.Count} image(s) through GraphQL.";
        }
        catch (Exception ex)
        {
            Status = $"Cannot load library: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenSelectedAsync()
    {
        if (IsBusy)
            return;

        var selected = SelectedImage;

        if (selected is null)
        {
            Status = "Select an image first.";
            return;
        }

        IsBusy = true;

        try
        {
            Status = "Loading original...";

            await OpenImageCoreAsync(selected);

            Status = $"{selected.OriginalFileName} — " +
                     $"{selected.Width} × {selected.Height} pixels";
        }
        catch (Exception)
        {
            Status = "Cannot open the selected image.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportImageAsync(
        Stream stream,
        string fileName)
    {
        if (IsBusy)
            return;

        IsBusy = true;

        try
        {
            Status = "Importing image...";

            using var content = new StreamContent(stream);

            content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(
                    "application/octet-stream");

            using var response = await Client.PostAsync(
                "images/import?fileName=" +
                Uri.EscapeDataString(fileName),
                content);

            if (!response.IsSuccessStatusCode)
            {
                Status = "Import failed: " +
                    await response.Content.ReadAsStringAsync();
                return;
            }

            var imported =
                await response.Content.ReadFromJsonAsync<ImageItem>();

            if (imported is null)
            {
                Status = "The server returned an empty import result.";
                return;
            }

            await RefreshLibraryCoreAsync();

            ImageItem? match = null;

            foreach (var image in Images)
            {
                if (image.Id == imported.Id)
                {
                    match = image;
                    break;
                }
            }

            if (match is null)
            {
                await LoadThumbnailAsync(imported);

                Images.Insert(0, imported);
                match = imported;
            }

            SelectedImage = match;

            await OpenImageCoreAsync(match);

            Status = $"Imported: {match.OriginalFileName}";
        }
        catch (Exception ex)
        {
            Status = $"Import or refresh failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyGrayscaleAsync()
    {
        await ProcessSelectedAsync("grayscale", "Grayscale");
    }

    public async Task ApplyAdjustmentsAsync()
    {
        var brightness = Brightness.ToString(
            "F2", CultureInfo.InvariantCulture);

        var contrast = Contrast.ToString(
            "F2", CultureInfo.InvariantCulture);

        await ProcessSelectedAsync(
            $"adjust?brightness={brightness}&contrast={contrast}",
            "Brightness and contrast");
    }

    private async Task ProcessSelectedAsync(
        string endpoint,
        string operationName)
    {
        if (IsBusy)
            return;

        var selected = SelectedImage;

        if (selected is null)
        {
            Status = "Select an image first.";
            return;
        }

        IsBusy = true;

        try
        {
            Status = $"{operationName}: processing...";

            using var content = new StringContent("");

            using var response = await Client.PostAsync(
                $"images/{selected.Id}/{endpoint}", content);

            if (!response.IsSuccessStatusCode)
            {
                Status = "Processing failed. Server returned " +
                         $"{(int)response.StatusCode}.";
                return;
            }

            var bytes =
                await response.Content.ReadAsByteArrayAsync();

            ShowPreview(bytes);

            Status = $"{operationName} complete: " +
                     $"{selected.OriginalFileName}. " +
                     "Output saved in App_Data/processed.";
        }
        catch (Exception)
        {
            Status = "Processing failed. Check the API connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ResetAdjustments()
    {
        Brightness = 0;
        Contrast = 1;

        Status =
            "Sliders reset. Click Open original to restore the view.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this, new PropertyChangedEventArgs(propertyName));
    }
}