using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorldNewsAlerts.Domain;
using WorldNewsAlerts.Infrastructure.Persistence;

namespace WorldNewsAlerts.Api;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=30000;");
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "User", "Admin" }) if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new(role));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (config["Admin:Email"] is { Length: > 0 } email && config["Admin:Password"] is { Length: > 0 } password)
        {
            var admin = await users.FindByEmailAsync(email);
            if (admin is null)
            {
                admin = new() { Email = email, UserName = email, DisplayName = "Administrator", EmailConfirmed = true };
                var result = await users.CreateAsync(admin, password);
                if (!result.Succeeded) throw new InvalidOperationException("Configured administrator could not be created. Use a strong password of at least ten characters.");
            }
            if (!await users.IsInRoleAsync(admin, "Admin")) await users.AddToRoleAsync(admin, "Admin");
        }
        if (config["Slack:Public:BotToken"] is { Length: > 0 } token && config["Slack:Public:TeamId"] is { Length: > 0 } teamId)
        {
            var workspace = await db.SlackWorkspaces.SingleOrDefaultAsync(w => w.TeamId == teamId);
            if (workspace is null) { workspace = new() { TeamId = teamId }; db.SlackWorkspaces.Add(workspace); }
            workspace.Name = config["Slack:Public:Name"] ?? "World News Alerts";
            workspace.ProtectedBotToken = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("slack-bot-token-v1").Protect(token);
            foreach (var channel in config.GetSection("Slack:Public:Channels").GetChildren())
            {
                if (!Catalog.Categories.ContainsKey(channel.Key) && channel.Key != "exceptional") throw new InvalidOperationException("Invalid public Slack category.");
                if (string.IsNullOrWhiteSpace(channel.Value)) continue;
                var targetKey = $"{teamId}:{channel.Value}";
                var destination = await db.Destinations.SingleOrDefaultAsync(d => d.ChannelKey == "slack" && d.TargetKey == targetKey);
                if (destination is null) { destination = new() { ChannelKey = "slack", TargetKey = targetKey, TargetIdentifier = channel.Value }; db.Destinations.Add(destination); }
                destination.Kind = "public_slack"; destination.PublicCategory = channel.Key; destination.SlackWorkspace = workspace;
                destination.Label = $"Public · {channel.Key.Replace('_', ' ')}";
            }
            await db.SaveChangesAsync();
        }
    }
}
