using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WorldNewsAlerts.Application;
using WorldNewsAlerts.Infrastructure.NewsProviders;
using WorldNewsAlerts.Infrastructure.NotificationChannels;

namespace WorldNewsAlerts.Tests;

public class ProviderTests
{
    [Fact]
    public void CodexAcceptsEmptyResultsButRejectsMalformedOrUnverifiableNews()
    {
        var now = DateTime.UtcNow;
        var request = new NewsFetchRequest(now.AddHours(-2), now, ["health"]);
        Assert.Empty(CodexNewsProvider.Parse(JsonSerializer.Serialize(new { retrievedAtUtc = now, events = Array.Empty<object>() }), request).Events);
        Assert.ThrowsAny<Exception>(() => CodexNewsProvider.Parse("not JSON", request));
        Assert.Throws<ArgumentException>(() => CodexNewsProvider.Parse(JsonSerializer.Serialize(new { retrievedAtUtc = now, events = new[] { new { title = "Missing fields" } } }), request));
        var source = new RetrievedSource("Test publisher", "https://news.example.com/health", now.AddMinutes(-1));
        var item = new RetrievedNewsEvent("A source-backed event", "A summary", ["health"], "significant", null, null, [source]);
        var result = new NewsFetchResult("codex", now, [item]);
        NewsRules.Validate(result, request);
        Assert.Throws<ArgumentException>(() => NewsRules.Validate(result with { Events = [item with { Sources = [] }] }, request));
        Assert.Throws<ArgumentException>(() => NewsRules.Validate(result with { Events = [item with { Sources = [source with { Url = "javascript:alert(1)" }] }] }, request));
        Assert.Throws<ArgumentException>(() => NewsRules.Validate(result with { Events = [item with { EventTimeUtc = now.AddDays(1) }] }, request));
        Assert.Throws<ArgumentException>(() => NewsRules.Validate(result with { Events = [item with { Sources = [source with { PublishedAtUtc = null }] }] }, request));
        Assert.Contains("\"sources\"", CodexNewsProvider.BuildPrompt(request));
        Assert.Contains("live web search", CodexNewsProvider.BuildPrompt(request));
    }
    [Theory]
    [InlineData("https://News.Example.com/story/?utm_source=mail#top", "https://news.example.com/story")]
    [InlineData("https://example.com/story?z=2&a=1&fbclid=abc", "https://example.com/story?a=1&z=2")]
    public void SourceUrlsNormalizeTrackingAndFragments(string url, string expected) => Assert.Equal(expected, NewsRules.NormalizeUrl(url));

    [Fact]
    public async Task ResendUsesBearerAuthHtmlTextAndStableIdempotencyKey()
    {
        var handler = new CapturingHandler(); using var http = new HttpClient(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Email:Resend:ApiKey"] = "test-key", ["Email:FromAddress"] = "News <news@example.com>" }).Build();
        var provider = new ResendEmailProvider(http, config);
        var message = new NotificationMessage(Guid.NewGuid(), "Your briefing", "<p>News</p>", "News");
        var result = await provider.SendAsync(message, "reader@example.com", default);
        Assert.Equal("email-123", result.ExternalMessageId);
        Assert.Equal("https://api.resend.com/emails", handler.Url);
        Assert.Equal("Bearer test-key", handler.Authorization);
        Assert.Equal($"world-news/{message.Id:N}", handler.Key);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("<p>News</p>", body.RootElement.GetProperty("html").GetString());
        Assert.Equal("News", body.RootElement.GetProperty("text").GetString());
        Assert.Equal("reader@example.com", body.RootElement.GetProperty("to")[0].GetString());
        var firstKey = handler.Key;
        await provider.SendAsync(message, "reader@example.com", default); Assert.Equal(firstKey, handler.Key);
        handler.Fail = true;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SendAsync(message, "reader@example.com", default));
        Assert.DoesNotContain("test-key", exception.Message);
    }
    private class CapturingHandler : HttpMessageHandler
    {
        public string? Url, Authorization, Key, Body; public bool Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.ToString(); Authorization = request.Headers.Authorization?.ToString();
            Key = request.Headers.GetValues("Idempotency-Key").Single(); Body = await request.Content!.ReadAsStringAsync(ct);
            return new(Fail ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK) { Content = new StringContent(Fail ? "upstream secret test-key" : "{\"id\":\"email-123\"}") };
        }
    }
}
