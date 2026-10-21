using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorldNewsAlerts.Application;
using WorldNewsAlerts.Domain;
using WorldNewsAlerts.Infrastructure.BackgroundJobs;
using WorldNewsAlerts.Infrastructure.Persistence;
using WorldNewsAlerts.Infrastructure.Services;

namespace WorldNewsAlerts.Tests;

public class ApiTests
{
    [Fact]
    public async Task CookieAuthRequiresCsrfAndVerifiedEmailAndProtectsAdmin()
    {
        using var app = new AppFactory(); using var client = app.Client();
        var noCsrf = await client.PostAsJsonAsync("/api/auth/register", new { email = "reader@example.com", password = TestAuth.Password, displayName = "Reader" });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscriptions")).StatusCode);
        var (id, token) = await TestAuth.RegisterAsync(client, "reader@example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { email = "reader@example.com", password = TestAuth.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Post, "/api/auth/verify-email", new { userId = id, token })).StatusCode);
        var login = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { email = "reader@example.com", password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.Contains("WorldNewsAlerts.Session") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Post, "/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
    [Fact]
    public async Task FilteringAndRepeatedRetrievalKeepCanonicalEvents()
    {
        using var app = new AppFactory(); using var client = app.Client();
        using (var scope = app.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<NewsService>();
            Assert.Equal(4, (await service.RetrieveAsync(default)).InsertedCount);
            Assert.Equal(0, (await service.RetrieveAsync(default)).InsertedCount);
        }
        var all = await client.GetFromJsonAsync<PageResult<NewsDto>>("/api/news"); Assert.Equal(4, all!.Total);
        var filtered = await client.GetFromJsonAsync<PageResult<NewsDto>>("/api/news?categories=technology_ai&period=6h"); Assert.Single(filtered!.Items);
        var exceptional = await client.GetFromJsonAsync<PageResult<NewsDto>>("/api/v1/news?importance=exceptional"); Assert.Equal(2, exceptional!.Total);
        var search = await client.GetFromJsonAsync<PageResult<NewsDto>>("/api/news?search=markets&pageSize=1"); Assert.Single(search!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/news?period=1y")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/news?categories=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/news?page=0")).StatusCode);
    }
    [Fact]
    public async Task InitialEmailsShareTransactionAndDigestSuppressesLaterDuplicate()
    {
        using var app = new AppFactory(); using var client = app.Client();
        var userId = await TestAuth.LoginAsync(client, "digest@example.com");
        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<NewsService>().RetrieveAsync(default);
        var request = TestAuth.Subscription("breaking_news", "geopolitics");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Post, "/api/subscriptions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Put, "/api/subscriptions", request)).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(await db.DeliveryJobs.Where(j => j.MessageType == "confirmation").ToListAsync());
            var digest = await db.DeliveryJobs.Include(j => j.Events).SingleAsync(j => j.MessageType == "digest");
            Assert.Single(digest.Events);
            var events = await db.NewsEvents.Include(e => e.Categories).Include(e => e.Sources).ToListAsync();
            await scope.ServiceProvider.GetRequiredService<OutboxService>().QueueMatchingAsync(events, default); await db.SaveChangesAsync();
            Assert.Equal(0, await db.DeliveryJobs.CountAsync(j => j.MessageType == "news"));
        }
        // New DI scope simulates dispatcher restart; jobs survive in SQLite.
        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(default);
        Assert.Equal(3, app.Email.Sent.Count); // verification + confirmation + digest
        var history = await client.GetFromJsonAsync<JsonElement>("/api/notifications"); Assert.Equal(3, history.GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task UnverifiedSubscriptionIsStoredButNewsJobsWaitUntilVerification()
    {
        using var app = new AppFactory(); using var client = app.Client();
        var (id, token) = await TestAuth.RegisterAsync(client, "pending@example.com");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == id);
            await scope.ServiceProvider.GetRequiredService<SubscriptionService>().SaveAsync(user, TestAuth.Subscription("health"), default);
            Assert.False(await db.DeliveryJobs.AnyAsync(j => j.MessageType == "confirmation" || j.MessageType == "digest"));
        }
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Post, "/api/auth/verify-email", new { userId = id, token })).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await db.DeliveryJobs.CountAsync(j => j.MessageType == "confirmation" || j.MessageType == "digest"));
            Assert.Contains("No qualifying events", (await db.DeliveryJobs.SingleAsync(j => j.MessageType == "digest")).TextBody);
        }
    }
    [Fact]
    public async Task FailedDeliveriesRetryTheSamePersistentJobs()
    {
        using var app = new AppFactory(); using var client = app.Client();
        await TestAuth.LoginAsync(client, "retry@example.com");
        await client.SendAsync(HttpMethod.Put, "/api/subscriptions", TestAuth.Subscription("health"));
        app.Email.Fail = true;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(default);
            Assert.Equal(3, await db.DeliveryJobs.CountAsync(j => j.Status == "RetryScheduled"));
            await db.DeliveryJobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        }
        app.Email.Fail = false;
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(default);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(3, await db.DeliveryJobs.CountAsync());
            Assert.Equal(3, await db.DeliveryJobs.CountAsync(j => j.Status == "Sent" && j.AttemptCount == 2));
        }
    }
    [Fact]
    public async Task DatabaseUniquenessProtectsSharedSlackAndEventDeliveries()
    {
        using var app = new AppFactory(); using var client = app.Client();
        var firstId = await TestAuth.LoginAsync(client, "first@example.com");
        await client.SendAsync(HttpMethod.Put, "/api/subscriptions", TestAuth.Subscription("health", "natural_disasters"));
        using var secondClient = app.Client();
        var secondId = await TestAuth.LoginAsync(secondClient, "second@example.com");
        await secondClient.SendAsync(HttpMethod.Put, "/api/subscriptions", TestAuth.Subscription("health"));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var destination = new NotificationDestination { ChannelKey = "slack", Kind = "public_slack", TargetKey = "T1:C1", TargetIdentifier = "C1", PublicCategory = "health" };
        db.Destinations.Add(destination);
        foreach (var subscription in await db.Subscriptions.ToListAsync())
            db.Add(new SubscriptionDestination { SubscriptionId = subscription.Id, DestinationId = destination.Id, Destination = destination });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<NewsService>().RetrieveAsync(default);
        Assert.Equal(1, await db.DeliveryJobs.CountAsync(j => j.DestinationId == destination.Id));
        var ledger = await db.DeliveryEvents.SingleAsync(e => e.DestinationId == destination.Id);
        db.DeliveryEvents.Add(new() { NewsEventId = ledger.NewsEventId, DestinationId = destination.Id, EventRevision = ledger.EventRevision, DeliveryJobId = ledger.DeliveryJobId });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task UnsubscribeCancelsPendingNewsAndAccountDeletionRemovesPrivateData()
    {
        using var app = new AppFactory(); using var client = app.Client();
        var id = await TestAuth.LoginAsync(client, "delete@example.com");
        var saved = await client.SendAsync(HttpMethod.Put, "/api/subscriptions", TestAuth.Subscription("health"));
        var subscription = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Delete, "/api/subscriptions/" + subscription.GetProperty("id").GetString())).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False((await db.Subscriptions.SingleAsync()).IsActive);
            Assert.Equal(2, await db.DeliveryJobs.CountAsync(j => j.Status == "Cancelled"));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Delete, "/api/profile", new { password = TestAuth.Password })).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Id == id));
            Assert.False(await db.DeliveryJobs.AnyAsync());
        }
    }
}
