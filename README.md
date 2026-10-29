\# Medical Image Analysis Workbench



A C# desktop application for organizing, viewing, processing, and

annotating research images.



Built with .NET 10, Avalonia, ASP.NET Core, GraphQL, Entity Framework

Core, SQLite, and SkiaSharp.



This is an educational portfolio project. It does not provide medical

diagnoses or clinical decision support.



\## Features



\- PNG and JPEG import with decoding validation.

\- Content-hash duplicate detection.

\- Managed storage that preserves original images.

\- Thumbnail previews and persistent image metadata.

\- Image viewing with zoom, fit-to-window, and scrollbar navigation.

\- Grayscale, brightness, contrast, thresholding, and resizing.

\- Rectangular annotations stored in original-image coordinates.

\- Annotation labels and notes with editing and deletion.

\- Filename and collection editing.

\- GraphQL search with paginated results.

\- Background batch processing with two workers.

\- Batch progress, cancellation requests, and output viewing.

\- Recovery of queued and interrupted jobs after backend restart.

\- Automated tests and a GitHub Actions workflow.

\- Reproducible processing benchmarks.



\## Architecture



```text

Avalonia desktop application

&#x20;         |

&#x20;         | GraphQL: metadata queries and selected mutations

&#x20;         | HTTP: image transfer, processing, batch operations

&#x20;         |

ASP.NET Core backend

&#x20;         |

&#x20;         +-- EF Core and SQLite

&#x20;         +-- Managed image and thumbnail storage

&#x20;         +-- Image-processing service

&#x20;         +-- Bounded queue and two background workers

```



\### Projects



| Project | Responsibility |

|---|---|

| Workbench.Core | Image, annotation, and processing-job entities |

| Workbench.Infrastructure | EF Core context and migrations |

| Workbench.Api | GraphQL, HTTP endpoints, storage, and processing |

| Workbench.Desktop | Avalonia interface and view models |

| Workbench.Tests | Database, processing, and worker tests |

| Workbench.Benchmarks | Repeatable processing benchmark |



\## Requirements



\- Windows x64.

\- .NET 10 SDK.

\- Internet access for the initial NuGet package restore.



The desktop application connects to the backend at:



```text

http://localhost:5050

```



\## Setup



Clone the repository:



```cmd

git clone https://github.com/shef-tech/MedicalImageWorkbench.git

cd MedicalImageWorkbench

```



Initialize the application:



```cmd

setup.cmd

```



Setup restores dependencies, builds the solution in Release mode,

runs automated tests, and applies SQLite migrations.



Stop running Workbench applications before rebuilding or running setup.



\## Start the application



In the first Command Prompt:



```cmd

start-api.cmd

```



Keep that window running.



In a second Command Prompt, enter the repository folder and run:



```cmd

start-desktop.cmd

```



The repository starts with an empty application database. Imported

images and runtime data are excluded from Git.



\## Example workflow



1\. Click \*\*Import image\*\* and select a PNG or JPEG.

2\. Select an image and click \*\*Open original\*\*.

3\. Use \*\*Fit to window\*\*, \*\*100%\*\*, or the zoom slider.

4\. Apply grayscale, brightness/contrast, thresholding, or resizing.

5\. Click \*\*Draw annotation\*\* and drag a rectangle.

6\. Enter a label and notes, then save.

7\. Click \*\*Show annotations\*\* to display saved rectangles.

8\. Use \*\*Manage annotations\*\* to edit labels/notes or delete rectangles.

9\. Use \*\*Edit metadata\*\* to assign a collection.

10\. Use \*\*Search library\*\* to search filenames or collections.

11\. Open \*\*Batch processing\*\*, choose images and an operation, then start.

12\. Inspect progress and completed outputs.



Processing operations start from the original image. They do not

automatically accumulate transformations from the current preview.



Batch settings are captured from the main window when the batch

window opens.



\## Storage



Runtime data is stored beneath:



```text

src/Workbench.Api/App\_Data/

```



This includes:



\- The SQLite database.

\- Managed original images.

\- Thumbnails.

\- Processed outputs.



Do not delete this directory if you want to retain imported images,

annotations, metadata, and processing history.



\## GraphQL



The local GraphQL endpoint and development editor are available at:



```text

http://localhost:5050/graphql

```



Example image query:



```graphql

query {

&#x20; images(search: "Experiment", skip: 0, take: 20) {

&#x20;   id

&#x20;   originalFileName

&#x20;   collectionName

&#x20;   width

&#x20;   height

&#x20; }

}

```



Available query fields:



\- `images`

\- `image`

\- `annotations`



Available mutation fields:



\- `addAnnotation`

\- `updateImageMetadata`



Other annotation changes and batch operations currently use HTTP

endpoints.



\## Background processing



A bounded in-memory queue holds up to 32 waiting job IDs.

Two workers process jobs concurrently.



Job records persist in SQLite. Queued jobs and jobs interrupted while

running are recovered when the backend starts.



Cancellation preserves completed jobs and requests cancellation of

unfinished work. Cancellation is cooperative: decoding, rendering, or

encoding already in progress may take time to stop.



The API records cancelled status when a cancellation request is

accepted; this does not mean every underlying operation has stopped

at that exact instant.



Progress measures finished images, including failed and cancelled jobs.

It does not measure progress within an individual image.



\## Automated tests



Run:



```cmd

dotnet test tests\\Workbench.Tests\\Workbench.Tests.csproj --configuration Release

```



The current suite contains 21 tests, including the template test.



Coverage includes:



\- SQLite metadata persistence.

\- Unique image-content hashes.

\- Annotation persistence and foreign-key behavior.

\- Processing-job persistence.

\- Pixel-level processing results.

\- Resize dimensions and parameter validation.

\- Corrupt inputs.

\- Cancellation before processing.

\- Active worker cancellation with a controlled processor.

\- Preservation of completed job outputs.



The worker tests use isolated temporary databases and controlled

processing. They do not constitute full HTTP endpoint coverage.



GitHub Actions builds the solution and runs the tests on Windows.



\## Performance benchmark



Run from the repository root:



```cmd

dotnet run --project tools\\Workbench.Benchmarks --configuration Release

```



The benchmark uses 24 synthetic 1024 × 768 PNG images and thresholding

at 128. It runs three measurements per concurrency setting, each in

a separate process.



Measured results on the development machine:



| Metric | Concurrency 1 | Concurrency 2 |

|---|---:|---:|

| Median elapsed time | 4.526 seconds | 2.442 seconds |

| Median throughput | 5.30 images/s | 9.83 images/s |

| Median process peak working set | 46.7 MiB | 71.8 MiB |

| Failed operations | 0 | 0 |



Measured speedup: \*\*1.85×\*\*, with a \*\*46.04% elapsed-time reduction\*\*.



These measurements include decoding, processing, PNG encoding, and

output writing. They exclude HTTP, queue dispatch, and SQLite.



Peak working set covers the child-process lifetime, including warm-up.

Results apply to this workload and machine; they are not a general

performance guarantee.



See \[benchmark details](docs/benchmarks/latest.md) and

\[raw measurements](docs/benchmarks/results.csv).



\## Current limitations



\- Windows is the verified initial target.

\- Initial formats are PNG and JPEG.

\- No DICOM or whole-slide image support.

\- Imported images are limited to 20 MB and 25 million pixels.

\- The main library initially loads 50 images.

\- Search provides separate pages of 20 results.

\- Batch selection uses images currently loaded in the main library.

\- Viewer navigation uses scrollbars rather than drag-to-pan.

\- Annotation geometry cannot yet be edited after saving.

\- Some library changes require Refresh library to reload thumbnails.

\- Processing implementations for individual HTTP requests and batch

&#x20; jobs are not yet fully consolidated.

\- Processing-history browsing is limited to the current batch window

&#x20; and batch status endpoints.

\- Full HTTP integration coverage and fault-injection testing remain

&#x20; future improvements.

\- Launch scripts require the .NET SDK; they are not a standalone installer.

\- No authentication, cloud deployment, or clinical certification.



\## Data and privacy



Use synthetic, public, or appropriately licensed de-identified images.



Do not import identifiable patient information into this educational

application.



Runtime databases, imported images, and generated outputs are excluded

from the repository.



\## Troubleshooting



\### Build reports that Workbench.Api.exe is locked



Stop the backend with Ctrl+C and close the desktop before rebuilding.



\### Desktop cannot connect



Start `start-api.cmd` first and verify that the backend listens on

port 5050.



\### Library is empty after cloning



This is expected. Run setup and import your own test images.



\### Database is missing



Run `setup.cmd` with the applications stopped.



\### Port 5050 is already in use



Stop any previous Workbench backend instance, then start it again.



\## Future improvements



\- Consolidate processing and validation across API paths.

\- Add full HTTP integration tests.

\- Add richer processing-history navigation.

\- Add drag-to-pan and annotation geometry editing.

\- Improve thumbnail lifecycle and loading feedback.

\- Publish and verify a standalone Windows distribution.

