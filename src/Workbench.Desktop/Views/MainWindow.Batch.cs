using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Workbench.Desktop.Views;

public class BatchAccepted
{
    public Guid BatchId { get; set; }
    public int Total { get; set; }
}

public class BatchJobResult
{
    public Guid Id { get; set; }
    public Guid ImageId { get; set; }

    public string Operation { get; set; } = "";
    public string Status { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public string? OutputUrl { get; set; }
}

public class BatchProgressResult
{
    public Guid BatchId { get; set; }

    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
    public int Running { get; set; }
    public int Queued { get; set; }
    public int ProgressPercent { get; set; }

    public bool IsFinished { get; set; }

    public BatchJobResult[] Jobs { get; set; } =
        Array.Empty<BatchJobResult>();
}

public partial class MainWindow
{
    private static readonly HttpClient BatchClient = new()
    {
        BaseAddress = new Uri("http://localhost:5050/")
    };

    private async void BatchProcessing_Click(
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

        if (_library.Images.Count == 0)
        {
            await ShowAnnotationMessageAsync(
                "Click Refresh library before opening batch processing.");
            return;
        }

        StopDrawing();

        var images = _library.Images.ToArray();

        // These settings are captured when this window opens.
        var settings = new
        {
            Brightness = (float)_library.Brightness,
            Contrast = (float)_library.Contrast,
            Cutoff = (int)_library.Threshold,
            Width = (int)(_library.ResizeWidth ?? 300)
        };

        var checks =
            new List<(Guid Id, CheckBox Control)>();

        var imagePanel = new StackPanel { Spacing = 6 };

        foreach (var image in images)
        {
            var check = new CheckBox
            {
                Content = image.OriginalFileName,
                IsChecked = true
            };

            checks.Add((image.Id, check));
            imagePanel.Children.Add(check);
        }

        var operationBox = new ComboBox
        {
            ItemsSource = new[]
            {
                "Grayscale",
                "Adjust",
                "Threshold",
                "Resize"
            },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var startButton = new Button
        {
            Content = "Start batch"
        };

        var cancelButton = new Button
        {
            Content = "Cancel batch",
            IsEnabled = false
        };

        var refreshButton = new Button
        {
            Content = "Check status",
            IsEnabled = false
        };

        var viewButton = new Button
        {
            Content = "View selected output",
            IsEnabled = false
        };

        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 20
        };

        var status = new TextBlock
        {
            Text = "Choose images and an operation.",
            TextWrapping = TextWrapping.Wrap
        };

        var cancellationStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        var batchIdBox = new TextBox
        {
            IsReadOnly = true,
            Watermark = "Batch ID appears after submission"
        };

        var resultsList = new ListBox
        {
            Height = 140
        };

        var buttons = new WrapPanel();

        foreach (var button in new[]
        {
            startButton,
            cancelButton,
            refreshButton,
            viewButton
        })
        {
            button.Margin = new Thickness(0, 0, 10, 8);
            buttons.Children.Add(button);
        }

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Batch image processing",
            FontSize = 20
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Select images:"
        });

        panel.Children.Add(new ScrollViewer
        {
            Height = 130,
            Content = imagePanel
        });

        panel.Children.Add(operationBox);

        panel.Children.Add(new TextBlock
        {
            Text =
                $"Settings: brightness {settings.Brightness:F0}, " +
                $"contrast {settings.Contrast:F2}, " +
                $"threshold {settings.Cutoff}, " +
                $"resize width {settings.Width}.",
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(buttons);
        panel.Children.Add(batchIdBox);
        panel.Children.Add(progressBar);
        panel.Children.Add(status);
        panel.Children.Add(cancellationStatus);
        panel.Children.Add(resultsList);

        panel.Children.Add(new TextBlock
        {
            Text =
                "Closing this window stops monitoring only. " +
                "Use Cancel batch to request cancellation.",
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new Window
        {
            Title = "Batch processing",
            Width = 760,
            Height = 800,
            MinWidth = 600,
            MinHeight = 500,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Content = panel
            }
        };

        using var monitorCancellation =
            new CancellationTokenSource();

        var token = monitorCancellation.Token;

        Guid? currentBatchId = null;

        BatchJobResult[] currentJobs =
            Array.Empty<BatchJobResult>();

        var submitting = false;
        var monitoring = false;
        var cancelling = false;
        var loadingOutput = false;
        var batchFinished = true;
        var cancellationRequested = false;
        var closed = false;

        void UpdateButtons()
        {
            if (closed)
                return;

            startButton.IsEnabled =
                !submitting &&
                !monitoring &&
                !cancelling &&
                batchFinished;

            cancelButton.IsEnabled =
                currentBatchId.HasValue &&
                !batchFinished &&
                !submitting &&
                !cancelling &&
                !cancellationRequested;

            refreshButton.IsEnabled =
                currentBatchId.HasValue &&
                !submitting &&
                !monitoring &&
                !cancelling;

            operationBox.IsEnabled =
                !submitting && batchFinished;

            imagePanel.IsEnabled =
                !submitting && batchFinished;

            viewButton.IsEnabled =
                !loadingOutput &&
                !submitting &&
                currentJobs.Any(job =>
                    job.Status == "Completed");
        }

        dialog.Closing += (_, args) =>
        {
            // Do not lose the submission response or interrupt
            // a cancellation request halfway through.
            if (submitting || cancelling || loadingOutput)
                args.Cancel = true;
        };

        dialog.Closed += (_, _) =>
        {
            closed = true;
            monitorCancellation.Cancel();
        };

        async Task CheckStatusAsync()
        {
            if (currentBatchId is null)
                return;

            var result =
                await BatchClient.GetFromJsonAsync<BatchProgressResult>(
                    $"batches/{currentBatchId}", token);

            if (result is null)
            {
                throw new InvalidOperationException(
                    "The server returned an empty batch status.");
            }

            if (closed)
                return;

            progressBar.Value = result.ProgressPercent;

            status.Text =
                $"{result.ProgressPercent}% — " +
                $"{result.Completed} completed, " +
                $"{result.Running} running, " +
                $"{result.Queued} queued, " +
                $"{result.Failed} failed, " +
                $"{result.Cancelled} cancelled.";

            var selectedJobId =
                resultsList.SelectedIndex >= 0 &&
                resultsList.SelectedIndex < currentJobs.Length
                    ? currentJobs[resultsList.SelectedIndex].Id
                    : (Guid?)null;

            currentJobs = result.Jobs;
            batchFinished = result.IsFinished;

            resultsList.ItemsSource = currentJobs.Select(job =>
            {
                var name = images.FirstOrDefault(
                    image => image.Id == job.ImageId)
                    ?.OriginalFileName ?? job.ImageId.ToString();

                return $"{name}: {job.Status}" +
                    (job.ErrorMessage is null
                        ? ""
                        : $" — {job.ErrorMessage}");
            }).ToArray();

            var selectedIndex = Array.FindIndex(
                currentJobs,
                job => job.Id == selectedJobId);

            resultsList.SelectedIndex = selectedIndex >= 0
                ? selectedIndex
                : currentJobs.Length > 0 ? 0 : -1;

            UpdateButtons();
        }

        async Task MonitorAsync()
        {
            if (monitoring || currentBatchId is null)
                return;

            monitoring = true;
            UpdateButtons();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await CheckStatusAsync();

                    if (batchFinished)
                        break;

                    await Task.Delay(1000, token);
                }
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                // The window closed.
            }
            catch (Exception ex)
            {
                if (!closed)
                {
                    status.Text =
                        $"Monitoring stopped: {ex.Message}. " +
                        "Click Check status to retry.";
                }
            }
            finally
            {
                monitoring = false;
                UpdateButtons();
            }
        }

        startButton.Click += async (_, _) =>
        {
            if (submitting || monitoring || !batchFinished)
                return;

            var selectedIds = checks
                .Where(item => item.Control.IsChecked == true)
                .Select(item => item.Id)
                .ToArray();

            if (selectedIds.Length == 0)
            {
                status.Text = "Select at least one image.";
                return;
            }

            submitting = true;
            UpdateButtons();

            try
            {
                status.Text = "Submitting batch...";
                cancellationStatus.Text = "";

                var request = new
                {
                    ImageIds = selectedIds,
                    Operation =
                        operationBox.SelectedItem?.ToString()
                        ?? "Grayscale",
                    Settings = settings
                };

                using var response =
                    await BatchClient.PostAsJsonAsync(
                        "batches", request, token);

                if (!response.IsSuccessStatusCode)
                {
                    var message =
                        await response.Content.ReadAsStringAsync();

                    throw new InvalidOperationException(message);
                }

                var accepted =
                    await response.Content
                        .ReadFromJsonAsync<BatchAccepted>(
                            cancellationToken: token);

                if (accepted is null ||
                    accepted.BatchId == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        "The server returned no valid batch ID.");
                }

                currentBatchId = accepted.BatchId;
                batchIdBox.Text = accepted.BatchId.ToString();

                batchFinished = false;
                cancellationRequested = false;
                progressBar.Value = 0;

                currentJobs =
                    Array.Empty<BatchJobResult>();

                resultsList.ItemsSource =
                    Array.Empty<string>();
            }
            catch (Exception ex)
            {
                status.Text =
                    $"Submission was not confirmed: {ex.Message}";

                return;
            }
            finally
            {
                submitting = false;
                UpdateButtons();
            }

            await MonitorAsync();
        };

        cancelButton.Click += async (_, _) =>
        {
            if (currentBatchId is null ||
                batchFinished ||
                cancelling)
            {
                return;
            }

            cancelling = true;
            UpdateButtons();

            try
            {
                using var content = new StringContent("");

                using var response =
                    await BatchClient.PostAsync(
                        $"batches/{currentBatchId}/cancel",
                        content,
                        token);

                response.EnsureSuccessStatusCode();

                cancellationRequested = true;

                cancellationStatus.Text =
                    "Cancellation requested. Completed outputs " +
                    "are preserved. Running operations may take " +
                    "a moment to stop.";
            }
            catch (Exception ex)
            {
                cancellationStatus.Text =
                    $"Cancellation was not confirmed: {ex.Message}";
            }
            finally
            {
                cancelling = false;
                UpdateButtons();
            }

            if (!monitoring)
                await MonitorAsync();
        };

        refreshButton.Click += async (_, _) =>
            await MonitorAsync();

        viewButton.Click += async (_, _) =>
        {
            var index = resultsList.SelectedIndex;

            if (index < 0 || index >= currentJobs.Length)
                return;

            var job = currentJobs[index];

            if (job.Status != "Completed" ||
                string.IsNullOrWhiteSpace(job.OutputUrl))
            {
                status.Text =
                    "Select a successfully completed job.";
                return;
            }

            loadingOutput = true;
            UpdateButtons();

            try
            {
                var bytes =
                    await BatchClient.GetByteArrayAsync(
                        job.OutputUrl);

                using var stream = new MemoryStream(bytes);
                using var bitmap = new Bitmap(stream);

                var previewWindow = new Window
                {
                    Title = $"Batch output — {job.Operation}",
                    Width = 800,
                    Height = 650,
                    WindowStartupLocation =
                        WindowStartupLocation.CenterOwner,
                    Content = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(16)
                    }
                };

                await previewWindow.ShowDialog(dialog);
            }
            catch (Exception ex)
            {
                status.Text =
                    $"Could not open output: {ex.Message}";
            }
            finally
            {
                loadingOutput = false;
                UpdateButtons();
            }
        };

        await dialog.ShowDialog(this);
    }
}