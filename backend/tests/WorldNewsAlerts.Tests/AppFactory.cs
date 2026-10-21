using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldNewsAlerts.Application;
using WorldNewsAlerts.Domain;

namespace WorldNewsAlerts.Tests;

public class RecordingChannel(string key) : INotificationChannel
{
    public string ChannelKey => key;
    public List<Guid> Sent { get; } = [];
    public bool Fail { get; set; }
    public Task<DeliveryResult> SendAsync(NotificationMessage message, NotificationDestination destination, CancellationToken ct)
    {
        if (Fail) throw new HttpRequestException("Simulated outage");
        Sent.Add(message.Id); return Task.FromResult(new DeliveryResult(message.Id.ToString()));
    }
}
public class AppFactory : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"world-news-tests-{Guid.NewGuid():N}.db");
    public RecordingChannel Email { get; } = new("email");
    public RecordingChannel Slack { get; } = new("slack");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={DatabasePath};Default Timeout=30;Pooling=False",
            ["Workers:Enabled"] = "false", ["Runtime:NewsProvider"] = "fake", ["Runtime:EmailProvider"] = "development",
            ["Serilog:MinimumLevel:Default"] = "Error"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<INotificationChannel>();
            services.AddSingleton<INotificationChannel>(Email); services.AddSingleton<INotificationChannel>(Slack);
        });
    }
    public HttpClient Client() => CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
            try { if (File.Exists(DatabasePath + suffix)) File.Delete(DatabasePath + suffix); } catch (IOException) { }
    }
}
public static class TestAuth
{
    public const string Password = "Strong-Test-Password9!";
    public static async Task<HttpResponseMessage> SendAsync(this HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("token").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    public static async Task<(string Id, string Token)> RegisterAsync(HttpClient client, string email)
    {
        var result = await client.SendAsync(HttpMethod.Post, "/api/auth/register", new { email, password = Password, displayName = "Test Reader" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var data = await result.Content.ReadFromJsonAsync<JsonElement>();
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(data.GetProperty("developmentVerificationUrl").GetString()!).Query);
        return (query["userId"].ToString(), query["token"].ToString());
    }
    public static async Task<string> LoginAsync(HttpClient client, string email)
    {
        var (id, token) = await RegisterAsync(client, email);
        var verified = await client.SendAsync(HttpMethod.Post, "/api/auth/verify-email", new { userId = id, token }); Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var signedIn = await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { email, password = Password }); Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        return id;
    }
    public static SubscriptionRequest Subscription(params string[] categories) => new(categories, "significant", true, []);
}
