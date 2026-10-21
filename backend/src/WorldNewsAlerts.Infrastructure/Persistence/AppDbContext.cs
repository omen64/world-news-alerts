using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WorldNewsAlerts.Domain;

namespace WorldNewsAlerts.Infrastructure.Persistence;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool IsDisabled { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<NewsEvent> NewsEvents => Set<NewsEvent>();
    public DbSet<NewsSource> NewsSources => Set<NewsSource>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<NotificationDestination> Destinations => Set<NotificationDestination>();
    public DbSet<SlackWorkspace> SlackWorkspaces => Set<SlackWorkspace>();
    public DbSet<SlackConnection> SlackConnections => Set<SlackConnection>();
    public DbSet<OAuthState> OAuthStates => Set<OAuthState>();
    public DbSet<DeliveryJob> DeliveryJobs => Set<DeliveryJob>();
    public DbSet<DeliveryEvent> DeliveryEvents => Set<DeliveryEvent>();
    public DbSet<NewsProviderExecution> ProviderExecutions => Set<NewsProviderExecution>();
    public DbSet<SystemSetting> Settings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Category>().HasData(Catalog.Categories.Select(c => new Category { Id = c.Key, Name = c.Value }));
        b.Entity<NewsEvent>().HasIndex(e => e.EventFingerprint).IsUnique();
        b.Entity<NewsEvent>().HasIndex(e => e.EffectiveAtUtc);
        b.Entity<NewsEvent>().HasMany(e => e.Categories).WithOne().HasForeignKey(c => c.NewsEventId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<NewsEvent>().HasMany(e => e.Sources).WithOne().HasForeignKey(c => c.NewsEventId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<NewsEventCategory>().HasKey(e => new { e.NewsEventId, e.CategoryId });
        b.Entity<NewsEventCategory>().HasOne<Category>().WithMany().HasForeignKey(e => e.CategoryId);
        b.Entity<NewsSource>().HasIndex(s => s.NormalizedUrl).IsUnique();
        b.Entity<Subscription>().HasIndex(s => s.UserId).IsUnique();
        b.Entity<Subscription>().HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Subscription>().HasMany(s => s.Categories).WithOne().HasForeignKey(c => c.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SubscriptionCategory>().HasKey(c => new { c.SubscriptionId, c.CategoryId });
        b.Entity<SubscriptionCategory>().HasOne<Category>().WithMany().HasForeignKey(c => c.CategoryId);
        b.Entity<Subscription>().HasMany(s => s.Destinations).WithOne().HasForeignKey(d => d.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SubscriptionDestination>().HasKey(d => new { d.SubscriptionId, d.DestinationId });
        b.Entity<SubscriptionDestination>().HasOne(d => d.Destination).WithMany().HasForeignKey(d => d.DestinationId);
        b.Entity<NotificationDestination>().HasIndex(d => new { d.ChannelKey, d.TargetKey }).IsUnique();
        b.Entity<NotificationDestination>().HasOne(d => d.SlackWorkspace).WithMany().HasForeignKey(d => d.SlackWorkspaceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SlackWorkspace>().HasIndex(w => w.TeamId).IsUnique();
        b.Entity<SlackConnection>().HasIndex(c => new { c.UserId, c.SlackWorkspaceId }).IsUnique();
        b.Entity<SlackConnection>().HasOne(c => c.Workspace).WithMany().HasForeignKey(c => c.SlackWorkspaceId);
        b.Entity<SlackConnection>().HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DeliveryJob>().HasOne(j => j.Destination).WithMany().HasForeignKey(j => j.DestinationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DeliveryJob>().HasIndex(j => new { j.Status, j.NextAttemptAtUtc });
        b.Entity<DeliveryJob>().HasMany(j => j.Events).WithOne(e => e.DeliveryJob).HasForeignKey(e => e.DeliveryJobId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DeliveryEvent>().HasIndex(e => new { e.NewsEventId, e.DestinationId, e.EventRevision }).IsUnique();
        b.Entity<DeliveryEvent>().HasOne<NewsEvent>().WithMany().HasForeignKey(e => e.NewsEventId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SystemSetting>().HasKey(s => s.Key);
        // SQLite stores UTC DateTimes as sortable text; restore UTC Kind when reading them.
        var converter = new ValueConverter<DateTime, DateTime>(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)) property.SetValueConverter(converter);
    }
}
