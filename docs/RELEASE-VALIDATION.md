\# Release Validation



Validation recorded on October 29, 2026.



\## Verified



\- Release build succeeds.

\- All 21 automated tests pass.

\- Initial GitHub Actions build-and-test workflow passes.

\- GraphQL image queries work from the desktop.

\- Annotation creation through GraphQL works.

\- Image viewing, thumbnails, and annotation overlays work.

\- Background batch processing completes and exposes saved output.

\- Developer setup applies database migrations successfully.

\- Self-contained Windows x64 applications publish successfully.

\- The standalone migration executable initializes the package database.

\- A clean ZIP archive was created and checked.

\- The archive excludes App\_Data, databases, temporary files,

&#x20; and debug symbols.

\- The ZIP was extracted into a separate folder.

\- The extracted backend starts from that folder on port 5050.

\- The extracted desktop opens, imports an image, and saves

&#x20; an annotation through GraphQL.



\## Automated test scope



The 21 tests cover database behavior, image-processing results,

invalid inputs, and background-worker behavior.



Active worker cancellation is tested using a controlled processor.

This is distinct from a complete HTTP cancellation test.



\## Benchmark evidence



Dataset: 24 synthetic PNG images, each 1024 × 768 pixels.

Operation: thresholding at 128.

Three measurements per concurrency setting.



| Metric | Concurrency 1 | Concurrency 2 |

|---|---:|---:|

| Median elapsed seconds | 4.526 | 2.442 |

| Median images per second | 5.30 | 9.83 |

| Median process peak working set, MiB | 46.7 | 71.8 |

| Failed operations | 0 | 0 |



Measured speedup: 1.85×.

Measured elapsed-time reduction: 46.04%.



These results apply to the measured workload and machine.

They exclude HTTP, queue dispatch, and SQLite.



\## Package instructions



1\. Extract the complete Windows x64 ZIP into a writable folder.

2\. Run package-start-api.cmd.

3\. Wait for the backend to listen on http://localhost:5050.

4\. Run package-start-desktop.cmd.

5\. Import your own PNG or JPEG images.



Keep the backend window running while using the desktop.



The package initializes its own database beneath api/App\_Data.

The published application folders include the .NET runtime.



\## Known limits and outstanding checks



\- A clean Windows machine without .NET installed has not been tested.

\- Full HTTP endpoint integration coverage remains incomplete.

\- Interrupted-job recovery needs a dedicated integration test.

\- Pagination beyond one page needs a larger-dataset test.

\- Saved image paths are absolute. Moving an already-used package

&#x20; can break existing image references.

\- Cancelled status records an accepted cancellation request;

&#x20; running native operations may take time to stop.

\- Screenshots containing third-party images require verified

&#x20; reuse permission and attribution before public publication.

\- The package is folder-based and is not a signed installer.

\- The application is educational software, not a clinical device.



\## Data handling



Do not include imported research images, patient information,

runtime databases, or processing outputs in source commits.



Keep test data out of distributable ZIP archives.

Preserve api/App\_Data when backing up a used package.

