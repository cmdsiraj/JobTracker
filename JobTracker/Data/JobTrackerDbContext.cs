using System.Text.Json;
using JobTracker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace JobTracker.Data;

public class JobTrackerDbContext(DbContextOptions<JobTrackerDbContext> options) : DbContext(options)
{
    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<EmailEvent> Events => Set<EmailEvent>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var stringListComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
            v => v.ToList());

        modelBuilder.Entity<JobApplication>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Tags)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new())
                .Metadata.SetValueComparer(stringListComparer);
            entity.Property(a => a.ThreadIds)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new())
                .Metadata.SetValueComparer(stringListComparer);
            entity.HasIndex(a => a.CompanyKey);
            entity.Ignore(a => a.SortedEvents);
            entity.Ignore(a => a.Status);

            entity.HasMany(a => a.Events)
                .WithOne(e => e.Application)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GmailMessageId);
            entity.Ignore(e => e.Kind);
            entity.Ignore(e => e.Direction);
            entity.Ignore(e => e.DetectedStatus);
            entity.Ignore(e => e.GmailUrl);
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Ignore(l => l.Type);
            entity.Ignore(l => l.Stage);
            entity.Ignore(l => l.LinkUrl);
        });
        modelBuilder.Entity<SyncState>(entity => entity.HasKey(s => s.Id));
    }

    private static readonly JsonSerializerOptions JsonOptions = new();
}
