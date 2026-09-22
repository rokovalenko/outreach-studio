using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OutreachStudio.Data;

public sealed class OutreachDbContext(DbContextOptions<OutreachDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserEvent> UserEvents => Set<UserEvent>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignVersion> CampaignVersions => Set<CampaignVersion>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<Suppression> Suppressions => Set<Suppression>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(u => u.Tier).HasConversion<string>();
            e.Property(u => u.Platform).HasConversion<string>();
            e.HasIndex(u => u.Email).IsUnique();
        });

        b.Entity<UserEvent>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>();
            e.HasIndex(x => new { x.UserId, x.Type, x.OccurredAt });
        });

        b.Entity<Campaign>(e =>
        {
            e.Property(c => c.Status).HasConversion<string>();
            e.HasMany(c => c.Versions).WithOne().HasForeignKey(v => v.CampaignId);
            e.HasMany(c => c.Approvals).WithOne().HasForeignKey(a => a.CampaignId);
        });

        b.Entity<CampaignVersion>(e =>
        {
            e.HasKey(v => new { v.CampaignId, v.Number });
            e.Property(v => v.RuleJson).HasColumnType("jsonb");
            e.Property(v => v.Channels).HasConversion<string>();
        });

        b.Entity<Delivery>(e =>
        {
            e.Property(d => d.Channel).HasConversion<string>();
            e.Property(d => d.Status).HasConversion<string>();
            e.Property(d => d.SkipReason).HasConversion<string>();
            // Exactly one row per user per campaign per channel, however many workers run.
            e.HasIndex(d => new { d.CampaignId, d.UserId, d.Channel }).IsUnique();
            // The claim query: due rows in Queued, oldest first.
            e.HasIndex(d => new { d.Status, d.DueAt });
            e.HasIndex(d => new { d.CampaignId, d.Status });
            e.HasIndex(d => new { d.UserId, d.SentAt });
            e.HasIndex(d => d.ProviderMessageId);
        });

        b.Entity<DeliveryAttempt>(e =>
        {
            e.HasIndex(a => a.StartedAt);
        });

        b.Entity<Suppression>(e =>
        {
            e.HasKey(s => s.Email);
        });
    }
}

/// <summary>Lets `dotnet ef migrations add` build the context without a running AppHost.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<OutreachDbContext>
{
    public OutreachDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OutreachDbContext>().UseNpgsql("Host=localhost;Database=outreach").Options);
}
