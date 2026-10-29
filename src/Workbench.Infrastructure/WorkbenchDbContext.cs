using Microsoft.EntityFrameworkCore;
using Workbench.Core;

namespace Workbench.Infrastructure;

public class WorkbenchDbContext : DbContext
{
    public WorkbenchDbContext(
        DbContextOptions<WorkbenchDbContext> options)
        : base(options)
    {
    }

    public DbSet<ImageRecord> Images => Set<ImageRecord>();

    public DbSet<Annotation> Annotations => Set<Annotation>();

    public DbSet<ProcessingJob> ProcessingJobs =>
        Set<ProcessingJob>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        var image = modelBuilder.Entity<ImageRecord>();

        image.HasKey(x => x.Id);

        image.Property(x => x.OriginalFileName)
            .IsRequired();

        image.Property(x => x.StoragePath)
            .IsRequired();

        image.Property(x => x.ContentHash)
            .IsRequired();

        image.HasIndex(x => x.ContentHash)
            .IsUnique();

        image.HasIndex(x => x.CollectionName);

        var annotation = modelBuilder.Entity<Annotation>();

        annotation.HasKey(x => x.Id);

        annotation.Property(x => x.Label)
            .IsRequired()
            .HasMaxLength(200);

        annotation.Property(x => x.Notes)
            .HasMaxLength(4000);

        annotation.HasIndex(x => x.ImageId);

        annotation.HasOne<ImageRecord>()
            .WithMany()
            .HasForeignKey(x => x.ImageId)
            .OnDelete(DeleteBehavior.Cascade);

        var job = modelBuilder.Entity<ProcessingJob>();

        job.HasKey(x => x.Id);

        job.Property(x => x.Operation)
            .HasConversion<string>()
            .IsRequired();

        job.Property(x => x.Status)
            .HasConversion<string>()
            .IsRequired();

        job.Property(x => x.ParametersJson)
            .IsRequired();

        job.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        job.HasIndex(x => x.BatchId);

        job.HasIndex(x => new
        {
            x.Status,
            x.CreatedAt
        });

        job.HasOne<ImageRecord>()
            .WithMany()
            .HasForeignKey(x => x.ImageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}