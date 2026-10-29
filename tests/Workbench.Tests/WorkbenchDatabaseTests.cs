using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workbench.Core;
using Workbench.Infrastructure;
using Xunit;

namespace Workbench.Tests;

public class WorkbenchDatabaseTests
{
    private static async Task<SqliteConnection>
        OpenDatabaseAsync()
    {
        var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=True");

        await connection.OpenAsync();

        return connection;
    }

    private static WorkbenchDbContext CreateContext(
        SqliteConnection connection)
    {
        var options =
            new DbContextOptionsBuilder<WorkbenchDbContext>()
                .UseSqlite(connection)
                .Options;

        return new WorkbenchDbContext(options);
    }

    private static ImageRecord CreateImage(
        string? contentHash = null)
    {
        return new ImageRecord
        {
            OriginalFileName = "sample.png",
            StoragePath = "test-storage/sample.png",
            ThumbnailPath = "test-storage/thumbnail.png",
            ContentHash = contentHash ?? Guid.NewGuid().ToString("N"),
            FileFormat = "Png",
            Width = 740,
            Height = 740,
            FileSize = 1000,
            CollectionName = "Test Experiment"
        };
    }

    [Fact]
    public async Task ImageMetadataSurvivesNewDatabaseContext()
    {
        await using var connection =
            await OpenDatabaseAsync();

        var image = CreateImage();

        await using (var db = CreateContext(connection))
        {
            await db.Database.EnsureCreatedAsync();

            db.Images.Add(image);

            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(connection))
        {
            var saved = await db.Images
                .SingleAsync(item => item.Id == image.Id);

            Assert.Equal("sample.png", saved.OriginalFileName);
            Assert.Equal("Test Experiment", saved.CollectionName);
            Assert.Equal(740, saved.Width);
            Assert.Equal(740, saved.Height);
            Assert.Equal(image.ContentHash, saved.ContentHash);
        }
    }

    [Fact]
    public async Task DuplicateContentHashIsRejectedByDatabase()
    {
        await using var connection =
            await OpenDatabaseAsync();

        await using var db = CreateContext(connection);

        await db.Database.EnsureCreatedAsync();

        db.Images.Add(CreateImage("same-content-hash"));

        await db.SaveChangesAsync();

        db.Images.Add(CreateImage("same-content-hash"));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AnnotationCoordinatesAndNotesArePersisted()
    {
        await using var connection =
            await OpenDatabaseAsync();

        var image = CreateImage();

        var annotation = new Annotation
        {
            ImageId = image.Id,
            Label = "Region of interest",
            Notes = "Persisted annotation",
            X = 100.5,
            Y = 120.25,
            Width = 200.75,
            Height = 150.5
        };

        await using (var db = CreateContext(connection))
        {
            await db.Database.EnsureCreatedAsync();

            db.Images.Add(image);
            db.Annotations.Add(annotation);

            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(connection))
        {
            var saved = await db.Annotations
                .SingleAsync(item => item.Id == annotation.Id);

            Assert.Equal(image.Id, saved.ImageId);
            Assert.Equal("Region of interest", saved.Label);
            Assert.Equal("Persisted annotation", saved.Notes);

            Assert.Equal(100.5, saved.X);
            Assert.Equal(120.25, saved.Y);
            Assert.Equal(200.75, saved.Width);
            Assert.Equal(150.5, saved.Height);
        }
    }

    [Fact]
    public async Task DeletingImageAlsoDeletesItsAnnotations()
    {
        await using var connection =
            await OpenDatabaseAsync();

        var image = CreateImage();

        await using (var db = CreateContext(connection))
        {
            await db.Database.EnsureCreatedAsync();

            db.Images.Add(image);

            db.Annotations.Add(new Annotation
            {
                ImageId = image.Id,
                Label = "Temporary region",
                X = 10,
                Y = 20,
                Width = 100,
                Height = 100
            });

            await db.SaveChangesAsync();
        }

        // Delete using a new context without loading annotations.
        // This verifies the database foreign-key cascade.
        await using (var db = CreateContext(connection))
        {
            var savedImage = await db.Images
                .SingleAsync(item => item.Id == image.Id);

            db.Images.Remove(savedImage);

            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(connection))
        {
            Assert.False(await db.Images.AnyAsync());
            Assert.False(await db.Annotations.AnyAsync());
        }
    }

    [Fact]
    public async Task ProcessingJobPersistsBatchAndStatusChanges()
    {
        await using var connection =
            await OpenDatabaseAsync();

        var image = CreateImage();
        var batchId = Guid.NewGuid();

        var job = new ProcessingJob
        {
            BatchId = batchId,
            ImageId = image.Id,
            Operation = ProcessingOperation.Threshold,
            ParametersJson = "{\"Cutoff\":128}"
        };

        await using (var db = CreateContext(connection))
        {
            await db.Database.EnsureCreatedAsync();

            db.Images.Add(image);
            db.ProcessingJobs.Add(job);

            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(connection))
        {
            var saved = await db.ProcessingJobs
                .SingleAsync(item => item.Id == job.Id);

            Assert.Equal(
                ProcessingJobStatus.Queued,
                saved.Status);

            Assert.Equal(batchId, saved.BatchId);

            saved.Status = ProcessingJobStatus.Completed;
            saved.StartedAt = DateTime.UtcNow;
            saved.CompletedAt = DateTime.UtcNow;
            saved.OutputPath = "test-storage/output.png";

            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(connection))
        {
            var saved = await db.ProcessingJobs
                .SingleAsync(item => item.Id == job.Id);

            Assert.Equal(
                ProcessingJobStatus.Completed,
                saved.Status);

            Assert.Equal(
                ProcessingOperation.Threshold,
                saved.Operation);

            Assert.Equal(
                "test-storage/output.png",
                saved.OutputPath);

            Assert.NotNull(saved.StartedAt);
            Assert.NotNull(saved.CompletedAt);
        }
    }

    [Fact]
    public async Task ImageWithProcessingHistoryCannotBeDeleted()
    {
        await using var connection =
            await OpenDatabaseAsync();

        var image = CreateImage();

        await using (var db = CreateContext(connection))
        {
            await db.Database.EnsureCreatedAsync();

            db.Images.Add(image);

            db.ProcessingJobs.Add(new ProcessingJob
            {
                ImageId = image.Id,
                BatchId = Guid.NewGuid(),
                Operation = ProcessingOperation.Grayscale
            });

            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(connection))
        {
            var savedImage = await db.Images
                .SingleAsync(item => item.Id == image.Id);

            db.Images.Remove(savedImage);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => db.SaveChangesAsync());
        }
    }
}