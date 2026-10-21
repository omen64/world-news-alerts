using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorldNewsAlerts.Application;
using WorldNewsAlerts.Domain;
using WorldNewsAlerts.Infrastructure;
using WorldNewsAlerts.Infrastructure.BackgroundJobs;
using WorldNewsAlerts.Infrastructure.Persistence;
using WorldNewsAlerts.Infrastructure.Services;

namespace WorldNewsAlerts.Api;

public static class ApiEndpoints
{
    private static string UserId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static object DestinationDto(NotificationDestination d) => new { d.Id, d.ChannelKey, d.Kind, d.Label, d.IsActive, d.PublicCategory };
    private static object SubscriptionDto(Subscription s) => new { s.Id, s.IsActive, s.MinimumImportance, s.InitialDigestLookbackHours, s.NotifyUpdates, s.CreatedAtUtc,
        categories = s.Categories.Select(c => c.CategoryId), emailEnabled = s.Destinations.Any(d => d.Destination.ChannelKey == "email"),
        slackDestinationIds = s.Destinations.Where(d => d.Destination.ChannelKey == "slack").Select(d => d.DestinationId) };
    private static void Check(IdentityResult result) { if (!result.Succeeded) throw new ArgumentException(string.Join(" ", result.Errors.Select(e => e.Description))); }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions { HttpOnly = false, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict });
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = tokens.RequestToken });
        });
        var auth = api.MapGroup("/auth").RequireRateLimiting("auth");
        auth.MapPost("/register", async (RegisterRequest body, UserManager<ApplicationUser> users, AppDbContext db, OutboxService outbox, DeliverySignal signal, IHostEnvironment env, CancellationToken ct) =>
        {
            if (!MailAddress.TryCreate(body.Email, out var address) || address.Address != body.Email || string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Length > 100) throw new ArgumentException("Enter a valid email and display name.");
            var user = new ApplicationUser { Email = body.Email, UserName = body.Email, DisplayName = body.DisplayName.Trim() };
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            Check(await users.CreateAsync(user, body.Password)); Check(await users.AddToRoleAsync(user, "User"));
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            var url = $"{outbox.BaseUrl}/verify-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
            await outbox.QueueIdentityAsync(user, "verification", "Verify your email", url, ct);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); signal.Wake();
            return Results.Ok(new { message = "Check your email to verify your account before signing in.", developmentVerificationUrl = env.IsDevelopment() || env.IsEnvironment("Testing") ? url : null });
        });
        auth.MapPost("/verify-email", async (VerifyRequest body, UserManager<ApplicationUser> users, AppDbContext db, OutboxService outbox, SubscriptionService subscriptions, OperationGates gates, DeliverySignal signal, CancellationToken ct) =>
        {
            var user = await users.FindByIdAsync(body.UserId) ?? throw new ArgumentException("Invalid verification link.");
            await gates.Writes.WaitAsync(ct);
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                Check(await users.ConfirmEmailAsync(user, body.Token));
                var subscription = await subscriptions.Query(user.Id).SingleOrDefaultAsync(ct);
                if (subscription is not null) await outbox.InitializeAsync(subscription, user, ct);
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); signal.Wake();
            }
            finally { gates.Writes.Release(); }
            return Results.Ok(new { message = "Email verified. You can now sign in." });
        });
        auth.MapPost("/login", async (LoginRequest body, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            var user = await users.FindByEmailAsync(body.Email);
            if (user is null || user.IsDisabled) return Results.Problem("Invalid credentials or account unavailable.", statusCode: 401);
            var result = await signIn.PasswordSignInAsync(user, body.Password, false, lockoutOnFailure: true);
            return result.Succeeded ? Results.Ok(new { message = "Signed in." }) : Results.Problem("Invalid credentials, unverified email, or account temporarily locked.", statusCode: 401);
        });
        auth.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
        api.MapGet("/auth/me", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = (await users.GetUserAsync(principal))!;
            return Results.Ok(new { user.Id, user.Email, user.DisplayName, user.EmailConfirmed, isAdmin = await users.IsInRoleAsync(user, "Admin") });
        }).RequireAuthorization();
        auth.MapPost("/forgot-password", async (EmailRequest body, UserManager<ApplicationUser> users, OutboxService outbox, AppDbContext db, DeliverySignal signal, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(body.Email);
            if (user is not null && user.EmailConfirmed && !user.IsDisabled)
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                var url = $"{outbox.BaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
                await outbox.QueueIdentityAsync(user, "password_reset", "Reset your password", url, ct);
                await db.SaveChangesAsync(ct); signal.Wake();
            }
            return Results.Ok(new { message = "If the account is eligible, a reset link has been queued." });
        });
        auth.MapPost("/resend-verification", async (EmailRequest body, UserManager<ApplicationUser> users, OutboxService outbox, AppDbContext db, DeliverySignal signal, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(body.Email);
            if (user is not null && !user.EmailConfirmed && !user.IsDisabled)
            {
                var token = await users.GenerateEmailConfirmationTokenAsync(user);
                await outbox.QueueIdentityAsync(user, "verification", "Verify your email", $"{outbox.BaseUrl}/verify-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}", ct);
                await db.SaveChangesAsync(ct); signal.Wake();
            }
            return Results.Ok(new { message = "If the account needs verification, an email has been queued." });
        });
        auth.MapPost("/reset-password", async (ResetRequest body, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByEmailAsync(body.Email) ?? throw new ArgumentException("Invalid reset link.");
            Check(await users.ResetPasswordAsync(user, body.Token, body.Password));
            return Results.Ok(new { message = "Password reset. You can sign in." });
        });
        api.MapGet("/catalog", async (SettingsStore settings, IConfiguration config, IHostEnvironment env, CancellationToken ct) =>
        {
            var runtime = await settings.GetAsync(ct);
            return Results.Ok(new { categories = Catalog.Categories.Select(c => new { id = c.Key, name = c.Value }), periods = Catalog.Periods,
                channels = new[] { new { key = "email", enabled = runtime.EmailEnabled }, new { key = "slack", enabled = runtime.SlackEnabled } },
                newsProvider = runtime.NewsProvider, developmentEmail = (env.IsDevelopment() || env.IsEnvironment("Testing")) && runtime.EmailProvider == "development",
                slackConfigured = !string.IsNullOrEmpty(config["Slack:ClientId"]), publicSlackInviteUrl = config["Slack:Public:InviteUrl"] });
        });
        api.MapGet("/news", async (string? categories, string? period, string? importance, string? search, int? page, int? pageSize, NewsService news, CancellationToken ct) =>
            Results.Ok(await news.BrowseAsync(categories, period ?? "24h", importance ?? "significant", search, page ?? 1, pageSize ?? 20, ct)));
        api.MapGet("/news/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var item = await db.NewsEvents.AsNoTracking().Include(e => e.Categories).Include(e => e.Sources).SingleOrDefaultAsync(e => e.Id == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(NewsRules.ToDto(item));
        });
        var member = api.MapGroup("").RequireAuthorization();
        member.MapGet("/subscriptions", async (ClaimsPrincipal principal, SubscriptionService subscriptions, CancellationToken ct) =>
        {
            var subscription = await subscriptions.Query(UserId(principal)).AsNoTracking().SingleOrDefaultAsync(ct);
            return Results.Ok(subscription is null ? null : SubscriptionDto(subscription));
        });
        async Task<IResult> SaveSubscription(SubscriptionRequest body, ClaimsPrincipal principal, UserManager<ApplicationUser> users, SubscriptionService subscriptions, CancellationToken ct)
            => Results.Ok(SubscriptionDto(await subscriptions.SaveAsync((await users.GetUserAsync(principal))!, body, ct)));
        member.MapPut("/subscriptions", SaveSubscription); member.MapPost("/subscriptions", SaveSubscription);
        member.MapDelete("/subscriptions/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db, SubscriptionService subscriptions, CancellationToken ct) =>
        {
            if (!await db.Subscriptions.AnyAsync(s => s.Id == id && s.UserId == UserId(principal), ct)) return Results.NotFound();
            await subscriptions.StopAsync(UserId(principal), ct); return Results.NoContent();
        });
        api.MapPost("/unsubscribe", async (UnsubscribeRequest body, OutboxService outbox, SubscriptionService subscriptions, CancellationToken ct) =>
        {
            string id;
            try { id = outbox.UnprotectUnsubscribe(body.Token); } catch (System.Security.Cryptography.CryptographicException) { throw new ArgumentException("The unsubscribe link is invalid or expired. Sign in to manage preferences."); }
            await subscriptions.StopAsync(id, ct); return Results.Ok(new { message = "All notifications have been stopped." });
        });
        member.MapGet("/notification-destinations", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var id = UserId(principal);
            var destinations = await db.Destinations.AsNoTracking().Where(d => d.UserId == id || d.Kind == "public_slack" || db.SlackConnections.Any(c => c.UserId == id && c.SlackWorkspaceId == d.SlackWorkspaceId && (d.Kind != "slack_dm" || d.TargetIdentifier == c.SlackUserId))).ToListAsync(ct);
            return Results.Ok(destinations.Select(DestinationDto));
        });
        member.MapGet("/notifications", async (ClaimsPrincipal principal, int? page, AppDbContext db, CancellationToken ct) =>
        {
            var number = page ?? 1; if (number < 1) throw new ArgumentException("Invalid page.");
            var id = UserId(principal);
            var query = db.DeliveryJobs.AsNoTracking().Where(j => j.UserId == id || (j.UserId == null && db.Subscriptions.Any(s => s.UserId == id && s.Destinations.Any(d => d.DestinationId == j.DestinationId))));
            return Results.Ok(new { items = await query.OrderByDescending(j => j.CreatedAtUtc).Skip((number - 1) * 20).Take(20)
                .Select(j => new { j.Id, j.Subject, j.MessageType, j.Status, j.AttemptCount, j.CreatedAtUtc, j.SentAtUtc, j.NextAttemptAtUtc, j.Error, channel = j.Destination.ChannelKey }).ToListAsync(ct), total = await query.CountAsync(ct), page = number, pageSize = 20 });
        });
        member.MapPut("/profile", async (ProfileRequest body, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            if (string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Length > 100) throw new ArgumentException("Display name must be 1–100 characters.");
            var user = (await users.GetUserAsync(principal))!; user.DisplayName = body.DisplayName.Trim(); Check(await users.UpdateAsync(user)); return Results.NoContent();
        });
        member.MapPost("/profile/change-password", async (ChangePasswordRequest body, ClaimsPrincipal principal, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        { var user = (await users.GetUserAsync(principal))!; Check(await users.ChangePasswordAsync(user, body.CurrentPassword, body.NewPassword)); await signIn.RefreshSignInAsync(user); return Results.NoContent(); });
        member.MapDelete("/profile", async ([Microsoft.AspNetCore.Mvc.FromBody] DeleteRequest body, ClaimsPrincipal principal, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AppDbContext db, SubscriptionService subscriptions, CancellationToken ct) =>
        {
            var user = (await users.GetUserAsync(principal))!;
            if (!await users.CheckPasswordAsync(user, body.Password)) return Results.Problem("Password is incorrect.", statusCode: 400);
            await subscriptions.StopAsync(user.Id, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var destinations = await db.Destinations.Where(d => d.UserId == user.Id).ToListAsync(ct); db.Destinations.RemoveRange(destinations);
            var history = await db.DeliveryJobs.Where(j => j.UserId == user.Id).ToListAsync(ct); db.DeliveryJobs.RemoveRange(history);
            await db.OAuthStates.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(ct);
            Check(await users.DeleteAsync(user)); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await signIn.SignOutAsync(); return Results.NoContent();
        });
        MapSlack(member); MapAdmin(api.MapGroup("/admin").RequireAuthorization(policy => policy.RequireRole("Admin")));
    }
    private static void MapSlack(RouteGroupBuilder member)
    {
        member.MapGet("/slack/connections", async (ClaimsPrincipal p, AppDbContext db, CancellationToken ct) => Results.Ok(await db.SlackConnections.AsNoTracking().Where(c => c.UserId == UserId(p)).Select(c => new { c.Id, c.SlackUserId, teamName = c.Workspace.Name, c.Workspace.IsActive }).ToListAsync(ct)));
        member.MapPost("/slack/connect", async (ClaimsPrincipal p, SlackService slack, HttpContext context, CancellationToken ct) =>
        {
            var result = await slack.ConnectAsync(UserId(p), ct);
            context.Response.Cookies.Append("SlackOAuthState", result.State, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromMinutes(10) });
            return Results.Ok(new { url = result.Url });
        });
        member.MapGet("/slack/callback", async (string? state, string? code, string? error, ClaimsPrincipal p, HttpContext context, SlackService slack, CancellationToken ct) =>
        {
            if (error is not null) return Results.LocalRedirect("/integrations/slack?error=authorization_cancelled");
            if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(code) || context.Request.Cookies["SlackOAuthState"] != state) throw new ArgumentException("Invalid Slack authorization state.");
            context.Response.Cookies.Delete("SlackOAuthState");
            await slack.CompleteAsync(UserId(p), state, code, ct); return Results.LocalRedirect("/integrations/slack?connected=true");
        });
        member.MapGet("/slack/connections/{id:guid}/channels", async (Guid id, ClaimsPrincipal p, SlackService slack, CancellationToken ct) => Results.Ok(await slack.ListChannelsAsync(UserId(p), id, ct)));
        member.MapPost("/slack/connections/{id:guid}/destinations", async (Guid id, ChannelRequest body, ClaimsPrincipal p, SlackService slack, CancellationToken ct) => Results.Ok(DestinationDto(await slack.AddChannelAsync(UserId(p), id, body.ChannelId, ct))));
        member.MapDelete("/slack/connections/{id:guid}", async (Guid id, ClaimsPrincipal p, SlackService slack, CancellationToken ct) => { await slack.DisconnectAsync(UserId(p), id, ct); return Results.NoContent(); });
    }
    private static void MapAdmin(RouteGroupBuilder admin)
    {
        admin.MapGet("/users", async (int? page, AppDbContext db, CancellationToken ct) =>
        {
            var number = page ?? 1; if (number < 1) throw new ArgumentException("Invalid page.");
            return Results.Ok(new { items = await db.Users.AsNoTracking().OrderBy(u => u.Email).Skip((number - 1) * 50).Take(50)
                .Select(u => new { u.Id, u.Email, u.DisplayName, u.EmailConfirmed, u.IsDisabled,
                    roles = db.UserRoles.Where(r => r.UserId == u.Id).Join(db.Roles, r => r.RoleId, r => r.Id, (r, role) => role.Name).ToList(),
                    categories = db.Subscriptions.Where(s => s.UserId == u.Id).SelectMany(s => s.Categories.Select(c => c.CategoryId)).ToList(),
                    destinations = db.Subscriptions.Where(s => s.UserId == u.Id).SelectMany(s => s.Destinations.Select(d => d.Destination.Label)).ToList() }).ToListAsync(ct), total = await db.Users.CountAsync(ct), page = number, pageSize = 50 });
        });
        admin.MapPut("/users/{id}", async (string id, DisableRequest body, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            if (id == UserId(principal)) throw new ArgumentException("You cannot disable your current administrator account.");
            var user = await users.FindByIdAsync(id); if (user is null) return Results.NotFound();
            user.IsDisabled = body.IsDisabled; Check(await users.UpdateAsync(user)); Check(await users.UpdateSecurityStampAsync(user)); return Results.NoContent();
        });
        admin.MapGet("/settings", async (SettingsStore settings, CancellationToken ct) => Results.Ok(await settings.GetAsync(ct)));
        admin.MapPut("/settings", async (RuntimeSettings body, SettingsStore settings, IHostEnvironment env, CancellationToken ct) =>
        {
            if (body.EmailProvider == "development" && !env.IsDevelopment() && !env.IsEnvironment("Testing")) throw new ArgumentException("Development email is unavailable in production.");
            await settings.SaveAsync(body, ct); return Results.NoContent();
        });
        admin.MapGet("/providers", async (SettingsStore settings, AppDbContext db, CancellationToken ct) =>
        {
            var options = await settings.GetAsync(ct);
            return Results.Ok(new { active = options.NewsProvider, providers = new[] { new { key = "codex", name = "Codex CLI" }, new { key = "fake", name = "Demo fixtures" } },
                lastExecution = await db.ProviderExecutions.AsNoTracking().OrderByDescending(e => e.StartedAtUtc).FirstOrDefaultAsync(ct) });
        });
        admin.MapPut("/providers/{key}", async (string key, SettingsStore settings, CancellationToken ct) => { var options = await settings.GetAsync(ct); options.NewsProvider = key; await settings.SaveAsync(options, ct); return Results.NoContent(); });
        admin.MapGet("/channels", async (SettingsStore settings, IConfiguration config, CancellationToken ct) =>
        {
            var options = await settings.GetAsync(ct);
            return Results.Ok(new { options.EmailEnabled, options.SlackEnabled, options.EmailProvider,
                emailProviders = new[] { new { key = "resend", name = "Resend", configured = !string.IsNullOrEmpty(config["Email:Resend:ApiKey"]) && !string.IsNullOrEmpty(config["Email:FromAddress"]) },
                    new { key = "smtp", name = "SMTP", configured = !string.IsNullOrEmpty(config["Email:Smtp:Host"]) && !string.IsNullOrEmpty(config["Email:FromAddress"]) },
                    new { key = "development", name = "Development inbox", configured = true } },
                slackConfigured = !string.IsNullOrEmpty(config["Slack:ClientId"]) });
        });
        admin.MapGet("/jobs", async (int? page, AppDbContext db, CancellationToken ct) =>
        {
            var number = page ?? 1; if (number < 1) throw new ArgumentException("Invalid page.");
            return Results.Ok(new { executions = await db.ProviderExecutions.AsNoTracking().OrderByDescending(e => e.StartedAtUtc).Skip((number - 1) * 30).Take(30).ToListAsync(ct),
                deliveries = await db.DeliveryJobs.AsNoTracking().OrderByDescending(j => j.CreatedAtUtc).Skip((number - 1) * 30).Take(30).Select(j => new { j.Id, j.Subject, j.Status, j.AttemptCount, j.CreatedAtUtc, j.Error, j.NextAttemptAtUtc }).ToListAsync(ct),
                deliveryCounts = await db.DeliveryJobs.GroupBy(j => j.Status).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct), page = number });
        });
        admin.MapPost("/jobs/retrieve-news", async (NewsService news, CancellationToken ct) => Results.Ok(await news.RetrieveAsync(ct)));
        admin.MapPost("/jobs/dispatch", async (NotificationDispatcher dispatcher, CancellationToken ct) => { await dispatcher.DispatchAsync(ct); return Results.NoContent(); });
        admin.MapPost("/jobs/{id:guid}/retry", async (Guid id, AppDbContext db, DeliverySignal signal, CancellationToken ct) =>
        {
            var count = await db.DeliveryJobs.Where(j => j.Id == id && (j.Status == "Failed" || j.Status == "RetryScheduled"))
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "Pending").SetProperty(j => j.NextAttemptAtUtc, DateTime.UtcNow).SetProperty(j => j.AttemptCount, 0), ct);
            if (count == 0) return Results.NotFound(); signal.Wake(); return Results.NoContent();
        });
        admin.MapPost("/channels/test-email", async (ClaimsPrincipal p, UserManager<ApplicationUser> users, OutboxService outbox, AppDbContext db, DeliverySignal signal, CancellationToken ct) =>
        {
            var user = (await users.GetUserAsync(p))!;
            await outbox.QueueIdentityAsync(user, "test", "Test email from World News Alerts", $"{outbox.BaseUrl}/admin/channels", ct);
            await db.SaveChangesAsync(ct); signal.Wake(); return Results.Ok(new { message = "Test email queued to your administrator email." });
        });
    }
}
public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record EmailRequest(string Email);
public record VerifyRequest(string UserId, string Token);
public record ResetRequest(string Email, string Token, string Password);
public record UnsubscribeRequest(string Token);
public record ProfileRequest(string DisplayName);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record DeleteRequest(string Password);
public record ChannelRequest(string ChannelId);
public record DisableRequest(bool IsDisabled);
