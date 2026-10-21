using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WorldNewsAlerts.Domain;
using WorldNewsAlerts.Infrastructure.Persistence;

namespace WorldNewsAlerts.Infrastructure.Services;

public class SlackApi(HttpClient http)
{
    public async Task<JsonElement> CallAsync(string method, string token, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://slack.com/api/{method}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Slack returned HTTP {(int)response.StatusCode}.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!json.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) throw new InvalidOperationException("Slack rejected the request. Check scopes and destination permissions.");
        return json.RootElement.Clone();
    }
}
public class SlackService(AppDbContext db, SlackApi api, HttpClient http, IConfiguration config, IDataProtectionProvider protection, OperationGates gates)
{
    private IDataProtector TokenProtector => protection.CreateProtector("slack-bot-token-v1");
    public string Token(SlackWorkspace workspace) => TokenProtector.Unprotect(workspace.ProtectedBotToken);
    private string RedirectUri => config["Slack:RedirectUri"] ?? "http://localhost:4200/api/slack/callback";
    public async Task<(string Url, string State)> ConnectAsync(string userId, CancellationToken ct)
    {
        var clientId = config["Slack:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(config["Slack:ClientSecret"])) throw new ArgumentException("Slack OAuth is not configured by the administrator.");
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.OAuthStates.Add(new() { Id = Hash(state), UserId = userId, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10) });
        await db.SaveChangesAsync(ct);
        return ($"https://slack.com/oauth/v2/authorize?client_id={Uri.EscapeDataString(clientId)}&scope={Uri.EscapeDataString("chat:write,channels:read,groups:read,im:write")}&redirect_uri={Uri.EscapeDataString(RedirectUri)}&state={state}", state);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async Task CompleteAsync(string userId, string state, string code, CancellationToken ct)
    {
        var hash = Hash(state);
        var consumed = await db.OAuthStates.Where(s => s.Id == hash && s.UserId == userId && s.ExpiresAtUtc > DateTime.UtcNow).ExecuteDeleteAsync(ct);
        if (consumed != 1) throw new ArgumentException("Slack authorization state expired or was already used.");
        using var response = await http.PostAsync("https://slack.com/api/oauth.v2.access", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client_id"] = config["Slack:ClientId"]!, ["client_secret"] = config["Slack:ClientSecret"]!, ["code"] = code, ["redirect_uri"] = RedirectUri }), ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var data = document.RootElement;
        if (!data.GetProperty("ok").GetBoolean()) throw new ArgumentException("Slack installation failed. Please reconnect.");
        var team = data.GetProperty("team");
        var teamId = team.GetProperty("id").GetString()!;
        var member = data.GetProperty("authed_user").GetProperty("id").GetString()!;
        await gates.Writes.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var workspace = await db.SlackWorkspaces.SingleOrDefaultAsync(w => w.TeamId == teamId, ct);
            if (workspace is null) { workspace = new() { TeamId = teamId }; db.SlackWorkspaces.Add(workspace); }
            workspace.Name = team.GetProperty("name").GetString()!; workspace.IsActive = true;
            workspace.ProtectedBotToken = TokenProtector.Protect(data.GetProperty("access_token").GetString()!);
            var connection = await db.SlackConnections.SingleOrDefaultAsync(c => c.UserId == userId && c.SlackWorkspaceId == workspace.Id, ct);
            if (connection is null) { connection = new() { UserId = userId, SlackWorkspaceId = workspace.Id, Workspace = workspace }; db.SlackConnections.Add(connection); }
            connection.SlackUserId = member;
            await db.SaveChangesAsync(ct);
            await EnsureDestinationAsync(workspace, member, "slack_dm", $"Direct messages · {workspace.Name}", ct);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        finally { gates.Writes.Release(); }
    }
    private async Task<NotificationDestination> EnsureDestinationAsync(SlackWorkspace workspace, string target, string kind, string label, CancellationToken ct)
    {
        var key = $"{workspace.TeamId}:{target}";
        var destination = await db.Destinations.SingleOrDefaultAsync(d => d.ChannelKey == "slack" && d.TargetKey == key, ct);
        if (destination is null)
        {
            destination = new() { ChannelKey = "slack", Kind = kind, TargetIdentifier = target, TargetKey = key,
                Label = label, SlackWorkspaceId = workspace.Id, SlackWorkspace = workspace };
            db.Destinations.Add(destination);
        }
        destination.IsActive = true;
        return destination;
    }
    public async Task<object> ListChannelsAsync(string userId, Guid connectionId, CancellationToken ct)
    {
        var connection = await db.SlackConnections.Include(c => c.Workspace).SingleOrDefaultAsync(c => c.Id == connectionId && c.UserId == userId, ct)
            ?? throw new ArgumentException("Unknown workspace connection.");
        var result = new List<object>(); string? cursor = null;
        do
        {
            var data = await api.CallAsync("conversations.list", Token(connection.Workspace), new { types = "public_channel,private_channel", exclude_archived = true, limit = 200, cursor }, ct);
            foreach (var channel in data.GetProperty("channels").EnumerateArray())
                if (channel.TryGetProperty("is_member", out var member) && member.GetBoolean()) result.Add(new { id = channel.GetProperty("id").GetString(), name = channel.GetProperty("name").GetString() });
            cursor = data.TryGetProperty("response_metadata", out var metadata) && metadata.TryGetProperty("next_cursor", out var next) ? next.GetString() : null;
        } while (!string.IsNullOrEmpty(cursor));
        return result;
    }
    public async Task<NotificationDestination> AddChannelAsync(string userId, Guid connectionId, string channelId, CancellationToken ct)
    {
        var connection = await db.SlackConnections.Include(c => c.Workspace).SingleOrDefaultAsync(c => c.Id == connectionId && c.UserId == userId, ct)
            ?? throw new ArgumentException("Unknown workspace connection.");
        var data = await api.CallAsync("conversations.info", Token(connection.Workspace), new { channel = channelId }, ct);
        var channel = data.GetProperty("channel");
        if (!channel.GetProperty("is_member").GetBoolean() || channel.GetProperty("is_archived").GetBoolean()) throw new ArgumentException("Invite the bot to this channel first.");
        await gates.Writes.WaitAsync(ct);
        try
        {
            var destination = await EnsureDestinationAsync(connection.Workspace, channelId, "slack_channel", $"#{channel.GetProperty("name").GetString()} · {connection.Workspace.Name}", ct);
            await db.SaveChangesAsync(ct); return destination;
        }
        finally { gates.Writes.Release(); }
    }
    public async Task DisconnectAsync(string userId, Guid id, CancellationToken ct)
    {
        var connection = await db.SlackConnections.SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct) ?? throw new ArgumentException("Unknown workspace connection.");
        await gates.Writes.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var subscriptions = await db.Subscriptions.Include(s => s.Destinations).ThenInclude(d => d.Destination).Where(s => s.UserId == userId).ToListAsync(ct);
            foreach (var subscription in subscriptions)
                db.RemoveRange(subscription.Destinations.Where(d => d.Destination.SlackWorkspaceId == connection.SlackWorkspaceId && d.Destination.Kind != "public_slack"));
            db.SlackConnections.Remove(connection);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        finally { gates.Writes.Release(); }
    }
}
