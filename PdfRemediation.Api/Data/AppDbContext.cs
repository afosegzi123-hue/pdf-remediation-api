using Microsoft.EntityFrameworkCore;
using PdfRemediation.Api.Models;

namespace PdfRemediation.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<BatchSession> BatchSessions { get; set; }
    public DbSet<RemediationLog> RemediationLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // BatchSession Configuration
        modelBuilder.Entity<BatchSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");
            
            entity.Property(e => e.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("NOW()");

            entity.Property(e => e.TotalFiles).IsRequired();
            entity.Property(e => e.SuccessfulFiles).IsRequired();
            entity.Property(e => e.FailedFiles).IsRequired();
            
            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(50);

            entity.HasMany(e => e.RemediationLogs)
                .WithOne(e => e.BatchSession)
                .HasForeignKey(e => e.BatchSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // RemediationLog Configuration
        modelBuilder.Entity<RemediationLog>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");
            
            entity.Property(e => e.BatchSessionId).IsRequired();
            
            entity.Property(e => e.OriginalFileName)
                .IsRequired()
                .HasMaxLength(255);
            
            entity.Property(e => e.FileSizeBytes).IsRequired();
            
            entity.Property(e => e.IsOcrApplied)
                .IsRequired()
                .HasDefaultValue(false);
            
            entity.Property(e => e.IsStructureRebuilt)
                .IsRequired()
                .HasDefaultValue(false);
            
            entity.Property(e => e.IsAccessibleTagged)
                .IsRequired()
                .HasDefaultValue(false);
            
            entity.Property(e => e.ProcessingDurationMs).IsRequired();
            
            entity.Property(e => e.ErrorMessage).HasColumnType("text");
        });
    }
}
