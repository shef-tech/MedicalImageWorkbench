using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using System.Security.Cryptography;
using Workbench.Api;
using Workbench.Core;
using Workbench.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 20 * 1024 * 1024);

var dataDirectory = Path.Combine(
    builder.Environment.ContentRootPath, "App_Data");

var imagesDirectory = Path.Combine(
    dataDirectory, "images");

Directory.CreateDirectory(imagesDirectory);

var databasePath = Path.Combine(
    dataDirectory, "workbench.db");

builder.Services.AddDbContextFactory<WorkbenchDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

builder.Services
    .AddGraphQLServer()
    .AddQueryType<GraphQLQueries>()
    .AddMutationType<GraphQLMutations>();

builder.Services.AddSingleton<BackgroundJobQueue>();
builder.Services.AddSingleton<JobCancellationRegistry>();
builder.Services.AddSingleton<ThumbnailService>();

builder.Services.AddSingleton<
    IImageProcessingService,
    ImageProcessingService>();

builder.Services.AddHostedService<ProcessingWorker>();

var app = builder.Build();

app.MapGet("/", () =>
    "Medical Image Workbench API is running.");

app.MapGet("/images", async (
    WorkbenchDbContext db,
    CancellationToken cancellationToken) =>
{
    return await db.Images
        .AsNoTracking()
        .OrderByDescending(image => image.ImportedAt)
        .Take(50)
        .ToListAsync(cancellationToken);
});

app.MapPost("/images/import", async (
    HttpRequest request,
    WorkbenchDbContext db,
    ThumbnailService thumbnails,
    CancellationToken cancellationToken) =>
{
    var fileName = Path.GetFileName(
        request.Query["fileName"].ToString());

    if (string.IsNullOrWhiteSpace(fileName))
        return Results.BadRequest("Provide a fileName.");

    using var buffer = new MemoryStream();

    await request.Body.CopyToAsync(buffer, cancellationToken);

    var bytes = buffer.ToArray();

    if (bytes.Length == 0)
        return Results.BadRequest("The uploaded file is empty.");

    using var data = SKData.CreateCopy(bytes);
    using var codec = SKCodec.Create(data);

    if (codec is null)
        return Results.BadRequest("The image cannot be decoded.");

    var format = codec.EncodedFormat;

    if (format != SKEncodedImageFormat.Png &&
        format != SKEncodedImageFormat.Jpeg)
    {
        return Results.BadRequest(
            "Only PNG and JPEG are supported.");
    }

    if (codec.Info.Width < 1 ||
        codec.Info.Height < 1 ||
        (long)codec.Info.Width * codec.Info.Height > 25_000_000)
    {
        return Results.BadRequest(
            "Invalid dimensions or image exceeds 25 million pixels.");
    }

    using var bitmap = SKBitmap.Decode(bytes);

    if (bitmap is null)
        return Results.BadRequest("The image is invalid.");

    var hash = Convert.ToHexString(
        SHA256.HashData(bytes));

    if (await db.Images.AnyAsync(
        image => image.ContentHash == hash,
        cancellationToken))
    {
        return Results.Conflict(
            "This image has already been imported.");
    }

    var id = Guid.NewGuid();

    var extension = format == SKEncodedImageFormat.Png
        ? ".png"
        : ".jpg";

    var storagePath = Path.Combine(
        imagesDirectory, $"{id}{extension}");

    string? thumbnailPath = null;

    var record = new ImageRecord
    {
        Id = id,
        OriginalFileName = fileName,
        StoragePath = storagePath,
        ContentHash = hash,
        FileFormat = format.ToString(),
        Width = bitmap.Width,
        Height = bitmap.Height,
        FileSize = bytes.LongLength
    };

    try
    {
        await File.WriteAllBytesAsync(
            storagePath, bytes, cancellationToken);

        thumbnailPath = await thumbnails.CreateAsync(
            id, storagePath, cancellationToken);

        record.ThumbnailPath = thumbnailPath;

        db.Images.Add(record);

        await db.SaveChangesAsync(cancellationToken);
    }
    catch
    {
        if (File.Exists(storagePath))
            File.Delete(storagePath);

        if (thumbnailPath is not null &&
            File.Exists(thumbnailPath))
        {
            File.Delete(thumbnailPath);
        }

        throw;
    }

    return Results.Created(
        $"/images/{id}/file", record);
});

app.MapGet("/images/{id:guid}/file", async (
    Guid id,
    WorkbenchDbContext db,
    CancellationToken cancellationToken) =>
{
    var record = await db.Images
        .AsNoTracking()
        .SingleOrDefaultAsync(
            image => image.Id == id,
            cancellationToken);

    if (record is null || !File.Exists(record.StoragePath))
        return Results.NotFound();

    var contentType = record.FileFormat == "Png"
        ? "image/png"
        : "image/jpeg";

    return Results.File(record.StoragePath, contentType);
});

app.MapGet("/images/{id:guid}/thumbnail", async (
    Guid id,
    WorkbenchDbContext db,
    ThumbnailService thumbnails,
    CancellationToken cancellationToken) =>
{
    var record = await db.Images
        .SingleOrDefaultAsync(
            image => image.Id == id,
            cancellationToken);

    if (record is null)
        return Results.NotFound("Image not found.");

    if (!string.IsNullOrWhiteSpace(record.ThumbnailPath) &&
        File.Exists(record.ThumbnailPath))
    {
        return Results.File(record.ThumbnailPath, "image/png");
    }

    if (!File.Exists(record.StoragePath))
        return Results.NotFound("Original image not found.");

    var thumbnailPath = await thumbnails.CreateAsync(
        record.Id,
        record.StoragePath,
        cancellationToken);

    record.ThumbnailPath = thumbnailPath;

    await db.SaveChangesAsync(cancellationToken);

    return Results.File(thumbnailPath, "image/png");
});

app.MapProcessingEndpoints();
app.MapThresholdEndpoints();
app.MapResizeEndpoints();
app.MapAnnotationEndpoints();
app.MapBatchEndpoints();
app.MapBatchCancellationEndpoints();

app.MapGraphQL("/graphql");

app.Run();