# Image Processing Benchmark

Generated at: 2026-10-29T22:59:44.5774453+00:00

## Configuration

- Runtime: .NET 10.0.14
- Operating system: Microsoft Windows 10.0.26300
- Architecture: X64
- Logical processors: 8
- Dataset: 24 synthetic PNG images
- Image dimensions: 1024 × 768
- Generator: seeded System.Random, opaque random RGB pixels
- Operation: threshold at 128
- Repetitions: 3 per concurrency
- Each measurement uses a fresh child process
- Run ordering alternates between concurrency 1 and 2

## Results

| Metric | Concurrency 1 | Concurrency 2 |
|---|---:|---:|
| Median elapsed seconds | 4.526 | 2.442 |
| Median images/second | 5.30 | 9.83 |
| Median process peak working set, MiB | 46.7 | 71.8 |
| Total failed operations | 0 | 0 |

Elapsed-time reduction: 46.04%.

Speedup ratio: 1.85×.

A negative time reduction means concurrency 2 was slower.

## Measurement limits

Run this benchmark using Release configuration.

Timed work includes file decoding, thresholding, PNG encoding,
and output file writing. It excludes dataset generation,
process startup, warm-up, HTTP, job queues, and SQLite.

Peak working set covers the entire child-process lifetime,
including runtime startup and warm-up. It is not incremental
memory allocated only by the timed operation.

File-system caches are not cleared. Results depend on this
machine, workload, image sizes, and storage. Synthetic images
do not represent every medical research dataset.

Raw measurements are in results.csv.