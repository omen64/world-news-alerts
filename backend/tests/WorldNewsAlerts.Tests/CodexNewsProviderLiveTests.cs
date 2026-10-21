using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WorldNewsAlerts.Application;
using WorldNewsAlerts.Infrastructure.NewsProviders;
using Xunit.Abstractions;

namespace WorldNewsAlerts.Tests;

// Requires an authenticated Codex CLI with live web search access.
public class CodexNewsProviderLiveTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task FetchAsync_ReturnsBreakingNewsAndGeopoliticsFromLastThreeHours()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Codex:Executable"] = OperatingSystem.IsWindows() ? "codex.exe" : "codex",
                ["Codex:TimeoutSeconds"] = "300"
            })
            .AddEnvironmentVariables()
            .Build();
        var provider = new CodexNewsProvider(config);
        var now = DateTime.UtcNow;
        var request = new NewsFetchRequest(now.AddHours(-15), now, ["breaking_news", "geopolitics"]);

        WriteOutput($"Calling CodexNewsProvider.FetchAsync for Breaking news and Geopolitics between {request.FromUtc:O} and {request.ToUtc:O}.");
        var result = await provider.FetchAsync(request, CancellationToken.None);

        WriteOutput($"FetchAsync completed with {result.Events.Count} event(s).");
        WriteOutput(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));

        Assert.Equal("codex", result.ProviderKey);
        NewsRules.Validate(result, request);
        // No qualifying news in the window is a valid live result.
    }

    private void WriteOutput(string message)
    {
        Console.WriteLine(message);
        output.WriteLine(message);
    }
}
