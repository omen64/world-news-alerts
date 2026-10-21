using WorldNewsAlerts.Application;

namespace WorldNewsAlerts.Infrastructure.NewsProviders;

public class FakeNewsProvider : INewsProvider
{
    public string ProviderKey => "fake";
    public Task<NewsFetchResult> FetchAsync(NewsFetchRequest request, CancellationToken cancellationToken)
    {
        // Deliberately marked fixtures, never presented as verified live reporting.
        var time = request.ToUtc.Date.AddHours(request.ToUtc.Hour);
        RetrievedNewsEvent Item(string title, string[] categories, string importance, string location, string slug) =>
            new($"[DEMO] {title}", "Fictional development fixture for testing filters, subscriptions and delivery. Switch the provider to Codex for live source-backed retrieval.",
                categories, importance, location, time, [new("Example fixture", $"https://example.com/demo/{slug}/{time:yyyy-MM-dd-HH}", time)]);
        IReadOnlyList<RetrievedNewsEvent> items = [
            Item("International climate summit reaches a milestone", ["geopolitics", "breaking_news"], "exceptional", "Global", "climate"),
            Item("New AI research collaboration announced", ["technology_ai"], "significant", "Europe", "ai"),
            Item("Markets respond to an economic policy announcement", ["market_movements", "economy"], "significant", "Global", "markets"),
            Item("International emergency response exercise begins", ["natural_disasters", "health"], "exceptional", "Asia-Pacific", "response") ];
        return Task.FromResult(new NewsFetchResult(ProviderKey, DateTime.UtcNow, items));
    }
}
