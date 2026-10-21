using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WorldNewsAlerts.Domain;

namespace WorldNewsAlerts.Application;

public record NewsFetchRequest(DateTime FromUtc, DateTime ToUtc, IReadOnlyCollection<string> Categories);
public record RetrievedSource(string Publisher, string Url, DateTime? PublishedAtUtc);
public record RetrievedNewsEvent(string Title, string Summary, string[] Categories, string Importance,
    string? Location, DateTime? EventTimeUtc, RetrievedSource[] Sources);
public record NewsFetchResult(string ProviderKey, DateTime RetrievedAtUtc, IReadOnlyList<RetrievedNewsEvent> Events);
public interface INewsProvider
{
    string ProviderKey { get; }
    Task<NewsFetchResult> FetchAsync(NewsFetchRequest request, CancellationToken cancellationToken);
}
public record NotificationMessage(Guid Id, string Subject, string Html, string Text);
public record DeliveryResult(string? ExternalMessageId);
public interface INotificationChannel
{
    string ChannelKey { get; }
    Task<DeliveryResult> SendAsync(NotificationMessage message, NotificationDestination destination, CancellationToken cancellationToken);
}
public interface IEmailProvider
{
    string ProviderKey { get; }
    Task<DeliveryResult> SendAsync(NotificationMessage message, string recipient, CancellationToken cancellationToken);
}
public record SubscriptionRequest(string[] Categories, string MinimumImportance, bool EmailEnabled,
    Guid[] SlackDestinationIds, int InitialDigestLookbackHours = 24, bool IsActive = true, bool NotifyUpdates = false);
public record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public record NewsDto(Guid Id, string Title, string Summary, string Importance, string? Location,
    DateTime? EventTimeUtc, DateTime RetrievedAtUtc, DateTime EffectiveAtUtc, string[] Categories,
    RetrievedSource[] Sources, int Revision, string ProviderKey);

public static partial class NewsRules
{
    public static void Validate(NewsFetchResult result, NewsFetchRequest request)
    {
        if (result.RetrievedAtUtc.Kind != DateTimeKind.Utc || result.RetrievedAtUtc > DateTime.UtcNow.AddMinutes(5))
            throw new ArgumentException("Retrieval timestamp must be UTC and cannot be in the future.");
        foreach (var item in result.Events)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 500 || string.IsNullOrWhiteSpace(item.Summary) || item.Summary.Length > 10000)
                throw new ArgumentException("News title or summary is invalid.");
            if (item.Importance is not ("significant" or "exceptional") || item.Categories.Length == 0 || item.Categories.Any(c => !Catalog.Categories.ContainsKey(c)))
                throw new ArgumentException("News classification is invalid.");
            if (item.Sources.Length == 0 || item.Sources.Any(s => string.IsNullOrWhiteSpace(s.Publisher) || !IsPublicUrl(s.Url)))
                throw new ArgumentException("Every event needs valid public HTTP(S) sources.");
            var timestamps = item.Sources.Select(s => s.PublishedAtUtc).Append(item.EventTimeUtc).Where(t => t.HasValue).Select(t => t!.Value).ToArray();
            if (timestamps.Any(t => t.Kind != DateTimeKind.Utc || t > request.ToUtc.AddMinutes(5)))
                throw new ArgumentException("News timestamps must be UTC and cannot be in the future.");
            if (timestamps.Length == 0 || !timestamps.Any(t => t >= request.FromUtc && t <= request.ToUtc))
                throw new ArgumentException("An event or publication timestamp must fall within the requested window.");
        }
    }
    public static bool IsPublicUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var u)
        && u.Scheme is "https" or "http" && string.IsNullOrEmpty(u.UserInfo)
        && !u.IsLoopback && u.Host.Contains('.') && !System.Net.IPAddress.TryParse(u.Host, out _);
    public static string NormalizeUrl(string value)
    {
        var uri = new Uri(value);
        var builder = new UriBuilder(uri) { Fragment = "", Host = uri.Host.ToLowerInvariant() };
        builder.Query = string.Join("&", uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("fbclid=", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal));
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }
    public static string NormalizedTitle(string value) => NonWord().Replace(value.ToLowerInvariant(), " ").Trim();
    public static string Fingerprint(RetrievedNewsEvent item) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{NormalizedTitle(item.Title)}|{item.Location?.Trim().ToLowerInvariant()}|{(item.EventTimeUtc ?? item.Sources.Min(s => s.PublishedAtUtc))?.ToString("yyyy-MM-dd")}")));
    public static bool Matches(Subscription subscription, NewsEvent item) => subscription.IsActive
        && (subscription.MinimumImportance == "significant" || item.Importance == "exceptional")
        && subscription.Categories.Any(c => item.Categories.Any(n => n.CategoryId == c.CategoryId));
    public static NewsDto ToDto(NewsEvent e) => new(e.Id, e.Title, e.Summary, e.Importance, e.Location,
        e.EventTimeUtc, e.RetrievedAtUtc, e.EffectiveAtUtc, e.Categories.Select(c => c.CategoryId).ToArray(),
        e.Sources.Select(s => new RetrievedSource(s.Publisher, s.Url, s.PublishedAtUtc)).ToArray(), e.Revision, e.ProviderKey);
    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();
}
