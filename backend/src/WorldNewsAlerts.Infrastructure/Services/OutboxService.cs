using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WorldNewsAlerts.Application;
using WorldNewsAlerts.Domain;
using WorldNewsAlerts.Infrastructure.BackgroundJobs;
using WorldNewsAlerts.Infrastructure.Persistence;

namespace WorldNewsAlerts.Infrastructure.Services;

public class OutboxService(AppDbContext db, IConfiguration config, IDataProtectionProvider protection)
{
    public string BaseUrl => (config["App:PublicUrl"] ?? "http://localhost:4200").TrimEnd('/');
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
    public string UnsubscribeUrl(string userId)
    {
        var token = protection.CreateProtector("unsubscribe-v1").ToTimeLimitedDataProtector().Protect(userId, TimeSpan.FromDays(90));
        return $"{BaseUrl}/unsubscribe?token={Uri.EscapeDataString(token)}";
    }
    public string UnprotectUnsubscribe(string token) => protection.CreateProtector("unsubscribe-v1").ToTimeLimitedDataProtector().Unprotect(token);
    public async Task<NotificationDestination> EmailDestinationAsync(ApplicationUser user, CancellationToken ct)
    {
        var key = user.Email!.ToUpperInvariant();
        var destination = db.Destinations.Local.FirstOrDefault(d => d.ChannelKey == "email" && d.TargetKey == key)
            ?? await db.Destinations.SingleOrDefaultAsync(d => d.ChannelKey == "email" && d.TargetKey == key, ct);
        if (destination is not null) return destination;
        destination = new() { UserId = user.Id, ChannelKey = "email", Kind = "email", TargetIdentifier = user.Email!, TargetKey = key, Label = user.Email! };
        db.Destinations.Add(destination);
        return destination;
    }
    public async Task QueueIdentityAsync(ApplicationUser user, string type, string subject, string url, CancellationToken ct)
    {
        var destination = await EmailDestinationAsync(user, ct);
        AddJob(destination, user.Id, type, subject,
            $"<p>Hello {H(user.DisplayName)},</p><p><a href=\"{H(url)}\">{H(subject)}</a></p><p>If you did not request this, ignore this email.</p>",
            $"{subject}\n{url}\n\nIf you did not request this, ignore this email.");
    }
    private DeliveryJob AddJob(NotificationDestination destination, string? userId, string type, string subject, string html, string text)
    {
        var job = new DeliveryJob { Destination = destination, DestinationId = destination.Id, UserId = userId, MessageType = type, Subject = subject,
            HtmlBody = $"<!doctype html><html><body style=\"font-family:Arial,sans-serif;color:#182d36;max-width:640px;margin:32px auto;padding:24px\"><p style=\"letter-spacing:3px;color:#087f75\">WORLD NEWS ALERTS</p><h1>{H(subject)}</h1>{html}</body></html>", TextBody = text };
        db.DeliveryJobs.Add(job);
        return job;
    }
    public async Task InitializeAsync(Subscription subscription, ApplicationUser user, CancellationToken ct)
    {
        if (!user.EmailConfirmed || user.IsDisabled || !subscription.IsActive || subscription.InitializedAtUtc.HasValue) return;
        var email = await EmailDestinationAsync(user, ct);
        var names = string.Join(", ", subscription.Categories.Select(c => Catalog.Categories[c.CategoryId]));
        var channels = string.Join(", ", subscription.Destinations.Select(d => d.Destination.ChannelKey).Distinct());
        AddJob(email, user.Id, "confirmation", "Your news subscription is active",
            $"<p>Hello {H(user.DisplayName)},</p><p>Categories: {H(names)}</p><p>Importance: {H(subscription.MinimumImportance)}</p><p>Channels: {H(channels)}</p>{Footer(user.Id)}",
            $"Categories: {names}\nImportance: {subscription.MinimumImportance}\nChannels: {channels}\nManage preferences: {BaseUrl}/subscriptions\nUnsubscribe: {UnsubscribeUrl(user.Id)}");
        var since = DateTime.UtcNow.AddHours(-subscription.InitialDigestLookbackHours);
        var candidates = await db.NewsEvents.Include(e => e.Categories).Include(e => e.Sources).Where(e => e.EffectiveAtUtc >= since)
            .OrderByDescending(e => e.EffectiveAtUtc).ToListAsync(ct);
        var items = candidates.Where(e => NewsRules.Matches(subscription, e)).ToList();
        await QueueEventsAsync(email, user.Id, items, "digest", "Your initial world news digest", true, ct);
        subscription.InitializedAtUtc = DateTime.UtcNow;
    }
    private string Footer(string userId) => $"<hr><p><a href=\"{H(BaseUrl)}/subscriptions\">Manage preferences</a> · <a href=\"{H(UnsubscribeUrl(userId))}\">Unsubscribe</a></p>";
    public async Task QueueEventsAsync(NotificationDestination destination, string? userId, IReadOnlyList<NewsEvent> events,
        string type, string subject, bool includeEmpty, CancellationToken ct)
    {
        var eligible = new List<NewsEvent>();
        foreach (var item in events)
        {
            var exists = db.DeliveryEvents.Local.Any(e => e.NewsEventId == item.Id && e.DestinationId == destination.Id && e.EventRevision == item.Revision)
                || await db.DeliveryEvents.AnyAsync(e => e.NewsEventId == item.Id && e.DestinationId == destination.Id && e.EventRevision == item.Revision, ct);
            if (!exists) eligible.Add(item);
        }
        if (eligible.Count == 0 && !includeEmpty) return;
        var html = new StringBuilder(); var text = new StringBuilder();
        if (eligible.Count == 0) { html.Append("<p>No qualifying events were found for your selected period.</p>"); text.AppendLine("No qualifying events were found for your selected period."); }
        foreach (var item in eligible)
        {
            var categories = string.Join(", ", item.Categories.Select(c => Catalog.Categories[c.CategoryId]));
            html.Append($"<article><h2>{H(item.Title)}</h2><p>{H(item.Summary)}</p><p>{H(categories)} · {H(item.Importance)}</p><p>{H(item.Location)} · {H(item.EventTimeUtc?.ToString("u") ?? "Event time unknown")}</p><ul>");
            text.AppendLine($"{item.Title}\n{item.Summary}\n{categories} · {item.Importance}\n{item.Location} · {item.EventTimeUtc?.ToString("u") ?? "Event time unknown"}");
            foreach (var source in item.Sources)
            {
                html.Append($"<li><a href=\"{H(source.Url)}\">{H(source.Publisher)}</a></li>");
                text.AppendLine($"{source.Publisher}: {source.Url}");
            }
            html.Append("</ul></article><hr>"); text.AppendLine();
        }
        if (userId is not null) { html.Append(Footer(userId)); text.AppendLine($"Manage preferences: {BaseUrl}/subscriptions\nUnsubscribe: {UnsubscribeUrl(userId)}"); }
        var job = AddJob(destination, userId, type, subject, html.ToString(), text.ToString());
        foreach (var item in eligible)
        {
            var deliveryEvent = new DeliveryEvent { NewsEventId = item.Id, DestinationId = destination.Id, EventRevision = item.Revision, DeliveryJobId = job.Id };
            job.Events.Add(deliveryEvent);
            db.DeliveryEvents.Add(deliveryEvent);
        }
    }
    public async Task QueueMatchingAsync(IReadOnlyList<NewsEvent> events, CancellationToken ct)
    {
        var subscriptions = await db.Subscriptions.Include(s => s.Categories).Include(s => s.Destinations).ThenInclude(d => d.Destination).Where(s => s.IsActive).ToListAsync(ct);
        foreach (var subscription in subscriptions)
        {
            var user = await db.Users.FindAsync([subscription.UserId], ct);
            if (user is null || user.IsDisabled || !user.EmailConfirmed) continue;
            var matching = events.Where(e => NewsRules.Matches(subscription, e) && (e.Revision == 1 || subscription.NotifyUpdates)).ToList();
            foreach (var link in subscription.Destinations.Where(d => d.Destination.IsActive && d.Destination.Kind != "public_slack"))
                foreach (var item in matching)
                    await QueueEventsAsync(link.Destination, link.Destination.ChannelKey == "email" ? user.Id : null, [item], "news", item.Title, false, ct);
        }
        var publicDestinations = await db.Destinations.Where(d => d.IsActive && d.Kind == "public_slack").ToListAsync(ct);
        foreach (var destination in publicDestinations)
            foreach (var item in events.Where(e => e.Revision == 1 && (destination.PublicCategory == "exceptional" ? e.Importance == "exceptional" : e.Categories.Any(c => c.CategoryId == destination.PublicCategory))))
                await QueueEventsAsync(destination, null, [item], "news", item.Title, false, ct);
    }
}

public class SubscriptionService(AppDbContext db, OutboxService outbox, OperationGates gates, DeliverySignal signal)
{
    public IQueryable<Subscription> Query(string userId) => db.Subscriptions.Include(s => s.Categories).Include(s => s.Destinations).ThenInclude(d => d.Destination).Where(s => s.UserId == userId);
    public async Task<Subscription> SaveAsync(ApplicationUser user, SubscriptionRequest request, CancellationToken ct)
    {
        if (request.Categories is null || request.SlackDestinationIds is null || request.Categories.Length == 0 || request.Categories.Any(c => !Catalog.Categories.ContainsKey(c))
            || request.MinimumImportance is not ("significant" or "exceptional") || !Catalog.Periods.Values.Contains(request.InitialDigestLookbackHours)
            || (!request.EmailEnabled && request.SlackDestinationIds.Length == 0)) throw new ArgumentException("Choose valid categories, importance, lookback and at least one delivery channel.");
        await gates.Writes.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var subscription = await Query(user.Id).SingleOrDefaultAsync(ct);
            if (subscription is null) { subscription = new() { UserId = user.Id }; db.Subscriptions.Add(subscription); }
            else { db.RemoveRange(subscription.Categories); db.RemoveRange(subscription.Destinations); subscription.Categories.Clear(); subscription.Destinations.Clear(); await db.SaveChangesAsync(ct); }
            subscription.MinimumImportance = request.MinimumImportance; subscription.IsActive = request.IsActive;
            subscription.NotifyUpdates = request.NotifyUpdates; subscription.InitialDigestLookbackHours = request.InitialDigestLookbackHours;
            subscription.Categories = request.Categories.Distinct().Select(c => new SubscriptionCategory { SubscriptionId = subscription.Id, CategoryId = c }).ToList();
            var destinations = new List<NotificationDestination>();
            if (request.EmailEnabled) destinations.Add(await outbox.EmailDestinationAsync(user, ct));
            foreach (var id in request.SlackDestinationIds.Distinct())
            {
                var destination = await db.Destinations.SingleOrDefaultAsync(d => d.Id == id && d.ChannelKey == "slack" && d.IsActive, ct) ?? throw new ArgumentException("Unknown Slack destination.");
                var authorized = destination.Kind == "public_slack" || (destination.SlackWorkspaceId.HasValue
                    && await db.SlackConnections.AnyAsync(c => c.UserId == user.Id && c.SlackWorkspaceId == destination.SlackWorkspaceId
                        && (destination.Kind != "slack_dm" || c.SlackUserId == destination.TargetIdentifier), ct));
                if (!authorized) throw new ArgumentException("Slack destination is not connected to this account.");
                destinations.Add(destination);
            }
            subscription.Destinations = destinations.Select(d => new SubscriptionDestination { SubscriptionId = subscription.Id, DestinationId = d.Id, Destination = d }).ToList();
            await outbox.InitializeAsync(subscription, user, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            signal.Wake();
            return subscription;
        }
        finally { gates.Writes.Release(); }
    }
    public async Task StopAsync(string userId, CancellationToken ct)
    {
        await gates.Writes.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Subscriptions.Where(s => s.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);
            await db.DeliveryJobs.Where(j => j.UserId == userId && j.MessageType != "verification" && j.MessageType != "password_reset" && j.Status != "Sent")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Cancelled"), ct);
            await transaction.CommitAsync(ct);
        }
        finally { gates.Writes.Release(); }
    }
}
