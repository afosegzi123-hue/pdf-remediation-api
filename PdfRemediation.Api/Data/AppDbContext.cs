using Microsoft.EntityFrameworkCore;
using PdfRemediation.Api.Models;

namespace PdfRemediation.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<BatchSession> BatchSessions { get; set; } = null!;
        public DbSet<RemediationLog> RemediationLogs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BatchSession>()
                .HasMany(b => b.RemediationLogs)
                .WithOne(l => l.BatchSession)
                .HasForeignKey(l => l.BatchSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
