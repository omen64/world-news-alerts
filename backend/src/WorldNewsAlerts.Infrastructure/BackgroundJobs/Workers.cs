using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorldNewsAlerts.Application;
using WorldNewsAlerts.Infrastructure.Persistence;
using WorldNewsAlerts.Infrastructure.Services;

namespace WorldNewsAlerts.Infrastructure.BackgroundJobs;

public class DeliverySignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    public void Wake() => channel.Writer.TryWrite(true);
    public async Task WaitAsync(TimeSpan fallback, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(fallback);
        try { await channel.Reader.ReadAsync(timeout.Token); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}
public class NotificationDispatcher(AppDbContext db, IEnumerable<INotificationChannel> channels, SettingsStore settings, OperationGates gates)
{
    public async Task DispatchAsync(CancellationToken ct)
    {
        if (!await gates.Dispatch.WaitAsync(0, ct)) return;
        try
        {
            var options = await settings.GetAsync(ct);
            if (!options.NotificationsEnabled) return;
            var now = DateTime.UtcNow;
            var jobs = await db.DeliveryJobs.Include(j => j.Destination).ThenInclude(d => d.SlackWorkspace)
                .Where(j => ((j.Status == "Pending" || j.Status == "RetryScheduled") && j.NextAttemptAtUtc <= now)
                    || (j.Status == "Processing" && j.LeaseUntilUtc <= now))
                .OrderBy(j => j.CreatedAtUtc).Take(50).ToListAsync(ct);
            foreach (var job in jobs)
            {
                if ((job.Destination.ChannelKey == "email" && !options.EmailEnabled) || (job.Destination.ChannelKey == "slack" && !options.SlackEnabled)) continue;
                if (!job.Destination.IsActive || (job.Destination.SlackWorkspaceId.HasValue && job.Destination.SlackWorkspace?.IsActive != true))
                { job.Status = "Cancelled"; await db.SaveChangesAsync(ct); continue; }
                if (job.Destination.ChannelKey == "slack" && job.Destination.Kind != "public_slack")
                {
                    var destinationId = job.DestinationId;
                    var hasSubscriber = await db.Subscriptions.AnyAsync(s => s.IsActive
                        && s.Destinations.Any(d => d.DestinationId == destinationId)
                        && db.Users.Any(u => u.Id == s.UserId && u.EmailConfirmed && !u.IsDisabled)
                        && db.SlackConnections.Any(c => c.UserId == s.UserId && c.SlackWorkspaceId == job.Destination.SlackWorkspaceId
                            && (job.Destination.Kind != "slack_dm" || c.SlackUserId == job.Destination.TargetIdentifier)), ct);
                    if (!hasSubscriber) { job.Status = "Cancelled"; await db.SaveChangesAsync(ct); continue; }
                }
                if (job.UserId is not null)
                {
                    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == job.UserId, ct);
                    if (user is null || user.IsDisabled || (job.MessageType is not ("verification" or "password_reset") && !user.EmailConfirmed))
                    { job.Status = "Cancelled"; await db.SaveChangesAsync(ct); continue; }
                    if (job.MessageType is "news" or "digest" or "confirmation")
                    {
                        var subscription = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == job.UserId, ct);
                        if (subscription?.IsActive != true) { job.Status = "Cancelled"; await db.SaveChangesAsync(ct); continue; }
                    }
                }
                job.Status = "Processing"; job.LeaseUntilUtc = DateTime.UtcNow.AddMinutes(5); job.AttemptCount++;
                await db.SaveChangesAsync(ct);
                try
                {
                    var channel = channels.Single(c => c.ChannelKey == job.Destination.ChannelKey);
                    var result = await channel.SendAsync(new(job.Id, job.Subject, job.HtmlBody, job.TextBody), job.Destination, ct);
                    job.Status = "Sent"; job.SentAtUtc = DateTime.UtcNow; job.ExternalMessageId = result.ExternalMessageId; job.Error = null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    job.Status = job.AttemptCount >= options.MaximumRetryCount ? "Failed" : "RetryScheduled";
                    job.Error = $"Delivery failed ({ex.GetType().Name}). Check provider configuration or destination permissions.";
                    job.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(options.InitialRetryDelaySeconds * Math.Pow(2, job.AttemptCount - 1));
                }
                job.LeaseUntilUtc = null;
                await db.SaveChangesAsync(ct);
            }
        }
        finally { gates.Dispatch.Release(); }
    }
}
public class NotificationDeliveryWorker(IServiceScopeFactory scopes, DeliverySignal signal, IConfiguration config, ILogger<NotificationDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Workers:Enabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var seconds = 30;
            try
            {
                using var scope = scopes.CreateScope();
                seconds = (await scope.ServiceProvider.GetRequiredService<SettingsStore>().GetAsync(stoppingToken)).PollingIntervalSeconds;
                await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Delivery scan failed ({ErrorType})", ex.GetType().Name); }
            await signal.WaitAsync(TimeSpan.FromSeconds(seconds), stoppingToken);
        }
    }
}
public class NewsRetrievalWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<NewsRetrievalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Workers:Enabled", true)) return;
        DateTime lastAttempt = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var options = await scope.ServiceProvider.GetRequiredService<SettingsStore>().GetAsync(stoppingToken);
                if (options.RetrievalEnabled && DateTime.UtcNow >= lastAttempt.AddMinutes(options.RetrievalIntervalMinutes))
                {
                    await scope.ServiceProvider.GetRequiredService<NewsService>().RetrieveAsync(stoppingToken);
                    lastAttempt = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Retrieval scan failed ({ErrorType})", ex.GetType().Name); lastAttempt = DateTime.UtcNow; }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
