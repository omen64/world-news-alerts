using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WorldNewsAlerts.Domain;
using WorldNewsAlerts.Infrastructure.Persistence;

namespace WorldNewsAlerts.Infrastructure;

public class RuntimeSettings
{
    public string NewsProvider { get; set; } = "fake";
    public bool RetrievalEnabled { get; set; } = true;
    public int RetrievalIntervalMinutes { get; set; } = 60;
    public int LookbackMinutes { get; set; } = 120;
    public bool NotificationsEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public bool SlackEnabled { get; set; } = true;
    public string EmailProvider { get; set; } = "development";
    public int PollingIntervalSeconds { get; set; } = 30;
    public int MaximumRetryCount { get; set; } = 3;
    public int InitialRetryDelaySeconds { get; set; } = 60;
}
public class SettingsStore(AppDbContext db, IConfiguration config)
{
    public async Task<RuntimeSettings> GetAsync(CancellationToken ct)
    {
        var json = await db.Settings.AsNoTracking().Where(s => s.Key == "runtime").Select(s => s.Value).SingleOrDefaultAsync(ct);
        return json is null ? config.GetSection("Runtime").Get<RuntimeSettings>() ?? new() : System.Text.Json.JsonSerializer.Deserialize<RuntimeSettings>(json)!;
    }
    public async Task SaveAsync(RuntimeSettings settings, CancellationToken ct)
    {
        if (settings.NewsProvider is not ("fake" or "codex") || settings.EmailProvider is not ("smtp" or "resend" or "development")
            || settings.RetrievalIntervalMinutes is < 1 or > 1440 || settings.LookbackMinutes is < 1 or > 10080
            || settings.PollingIntervalSeconds is < 1 or > 300 || settings.MaximumRetryCount is < 1 or > 10 || settings.InitialRetryDelaySeconds is < 1 or > 3600)
            throw new ArgumentException("Invalid provider or scheduling settings.");
        var row = await db.Settings.FindAsync(["runtime"], ct);
        if (row is null) { row = new() { Key = "runtime" }; db.Settings.Add(row); }
        row.Value = System.Text.Json.JsonSerializer.Serialize(settings);
        await db.SaveChangesAsync(ct);
    }
}
public class OperationGates
{
    public SemaphoreSlim Writes { get; } = new(1, 1);
    public SemaphoreSlim Retrieval { get; } = new(1, 1);
    public SemaphoreSlim Dispatch { get; } = new(1, 1);
}
