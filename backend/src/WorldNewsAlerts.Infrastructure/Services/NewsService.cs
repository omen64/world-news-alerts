using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorldNewsAlerts.Application;
using WorldNewsAlerts.Domain;
using WorldNewsAlerts.Infrastructure.BackgroundJobs;
using WorldNewsAlerts.Infrastructure.Persistence;

namespace WorldNewsAlerts.Infrastructure.Services;

public class NewsService(AppDbContext db, IEnumerable<INewsProvider> providers, SettingsStore settings,
    OutboxService outbox, OperationGates gates, DeliverySignal signal, ILogger<NewsService> logger)
{
    public async Task<PageResult<NewsDto>> BrowseAsync(string? categories, string period, string importance, string? search, int page, int pageSize, CancellationToken ct)
    {
        if (!Catalog.Periods.TryGetValue(period, out var hours) || importance is not ("significant" or "exceptional") || page < 1 || page > 100000 || pageSize is < 1 or > 100)
            throw new ArgumentException("Invalid news filters or pagination.");
        var keys = (categories ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (keys.Any(k => !Catalog.Categories.ContainsKey(k))) throw new ArgumentException("Unknown category.");
        var since = DateTime.UtcNow.AddHours(-hours);
        var query = db.NewsEvents.AsNoTracking().Where(e => e.EffectiveAtUtc >= since);
        if (keys.Length > 0) query = query.Where(e => e.Categories.Any(c => keys.Contains(c.CategoryId)));
        if (importance == "exceptional") query = query.Where(e => e.Importance == "exceptional");
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim().ToLower(); query = query.Where(e => e.Title.ToLower().Contains(value) || e.Summary.ToLower().Contains(value)); }
        var total = await query.CountAsync(ct);
        var items = await query.Include(e => e.Categories).Include(e => e.Sources).OrderByDescending(e => e.EffectiveAtUtc).ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items.Select(NewsRules.ToDto).ToList(), total, page, pageSize);
    }
    public async Task<NewsProviderExecution> RetrieveAsync(CancellationToken ct)
    {
        if (!await gates.Retrieval.WaitAsync(0, ct)) throw new ArgumentException("A retrieval is already running.");
        NewsProviderExecution? execution = null;
        try
        {
            var options = await settings.GetAsync(ct);
            var provider = providers.Single(p => p.ProviderKey == options.NewsProvider);
            execution = new() { ProviderKey = provider.ProviderKey };
            db.ProviderExecutions.Add(execution); await db.SaveChangesAsync(ct);
            var now = DateTime.UtcNow;
            var lastSuccess = await db.ProviderExecutions.Where(e => e.Status == "Succeeded").OrderByDescending(e => e.StartedAtUtc).Select(e => (DateTime?)e.StartedAtUtc).FirstOrDefaultAsync(ct);
            var from = now.AddMinutes(-options.LookbackMinutes);
            if (lastSuccess.HasValue && lastSuccess.Value.AddMinutes(-15) < from) from = lastSuccess.Value.AddMinutes(-15);
            if (from < now.AddDays(-7)) from = now.AddDays(-7);
            var request = new NewsFetchRequest(from, now, Catalog.Categories.Keys.ToArray());
            var result = await provider.FetchAsync(request, ct);
            NewsRules.Validate(result, request);
            execution.EventsCount = result.Events.Count;
            execution.InsertedCount = await IngestAsync(result, ct);
            execution.Status = "Succeeded";
        }
        catch (OperationCanceledException)
        {
            if (execution is not null) { db.ChangeTracker.Clear(); db.ProviderExecutions.Attach(execution); execution.Status = "Cancelled"; execution.Error = "Retrieval was cancelled before completion."; }
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("News retrieval failed ({ErrorType})", ex.GetType().Name);
            // Never persist raw process/network exception text that may contain secrets.
            if (execution is null) throw;
            db.ChangeTracker.Clear(); db.ProviderExecutions.Attach(execution);
            execution.Status = "Failed";
            execution.Error = ex is ArgumentException ? "Provider output failed validation." : ex is TimeoutException ? "Provider timed out." : "Provider execution failed. Check configuration, CLI authentication and network access.";
        }
        finally
        {
            try
            {
                if (execution is not null) { execution.CompletedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(CancellationToken.None); }
            }
            finally { gates.Retrieval.Release(); }
        }
        return execution!;
    }
    public async Task<int> IngestAsync(NewsFetchResult result, CancellationToken ct)
    {
        await gates.Writes.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var changed = new List<NewsEvent>(); var inserted = 0;
            foreach (var input in result.Events)
            {
                var urls = input.Sources.Select(s => NewsRules.NormalizeUrl(s.Url)).Distinct().ToArray();
                var fingerprint = NewsRules.Fingerprint(input);
                var existing = db.NewsEvents.Local.FirstOrDefault(e => e.EventFingerprint == fingerprint || e.Sources.Any(s => urls.Contains(s.NormalizedUrl)))
                    ?? await db.NewsEvents.Include(e => e.Categories).Include(e => e.Sources)
                        .FirstOrDefaultAsync(e => e.EventFingerprint == fingerprint || e.Sources.Any(s => urls.Contains(s.NormalizedUrl)), ct);
                var time = input.EventTimeUtc ?? input.Sources.Min(s => s.PublishedAtUtc) ?? result.RetrievedAtUtc;
                if (existing is null)
                {
                    var candidates = await db.NewsEvents.Include(e => e.Categories).Include(e => e.Sources)
                        .Where(e => e.EffectiveAtUtc >= time.AddHours(-24) && e.EffectiveAtUtc <= time.AddHours(24) && e.Location == input.Location).ToListAsync(ct);
                    var words = NewsRules.NormalizedTitle(input.Title).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                    existing = candidates.FirstOrDefault(e => {
                        var other = NewsRules.NormalizedTitle(e.Title).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                        return words.Count > 0 && words.Intersect(other).Count() / (double)words.Union(other).Count() >= .85;
                    });
                }
                if (existing is null)
                {
                    existing = new() { Title = input.Title.Trim(), Summary = input.Summary.Trim(), Importance = input.Importance,
                        Location = input.Location, EventTimeUtc = input.EventTimeUtc, EffectiveAtUtc = time,
                        RetrievedAtUtc = result.RetrievedAtUtc, EventFingerprint = fingerprint, ProviderKey = result.ProviderKey };
                    db.NewsEvents.Add(existing); inserted++; changed.Add(existing);
                }
                else if (existing.Summary != input.Summary.Trim() || existing.Importance != input.Importance)
                {
                    existing.Summary = input.Summary.Trim(); existing.Importance = input.Importance; existing.Revision++;
                    if (!changed.Contains(existing)) changed.Add(existing);
                }
                foreach (var category in input.Categories.Distinct())
                    if (existing.Categories.All(c => c.CategoryId != category)) existing.Categories.Add(new() { NewsEventId = existing.Id, CategoryId = category });
                foreach (var source in input.Sources)
                {
                    var normalized = NewsRules.NormalizeUrl(source.Url);
                    if (db.NewsSources.Local.Any(s => s.NormalizedUrl == normalized) || await db.NewsSources.AnyAsync(s => s.NormalizedUrl == normalized, ct)) continue;
                    existing.Sources.Add(new() { NewsEventId = existing.Id, Publisher = source.Publisher.Trim(), Url = source.Url, NormalizedUrl = normalized, PublishedAtUtc = source.PublishedAtUtc });
                }
            }
            await db.SaveChangesAsync(ct);
            await outbox.QueueMatchingAsync(changed, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            signal.Wake();
            return inserted;
        }
        finally { gates.Writes.Release(); }
    }
}
