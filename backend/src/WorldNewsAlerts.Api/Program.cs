using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Serilog;
using WorldNewsAlerts.Api;
using WorldNewsAlerts.Infrastructure;
using WorldNewsAlerts.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true).AddEnvironmentVariables();
builder.Host.UseSerilog((context, services, log) => log.ReadFrom.Configuration(context.Configuration).WriteTo.Console());
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-XSRF-TOKEN"; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
var dataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys"))).SetApplicationName("WorldNewsAlerts");
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "WorldNewsAlerts.Session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(12); o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async c =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(c);
        if (c.Principal?.Identity?.IsAuthenticated != true) return;
        var users = c.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.GetUserAsync(c.Principal);
        if (user is null || user.IsDisabled) { c.RejectPrincipal(); await c.HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync(); }
    };
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddProblemDetails(); builder.Services.AddOpenApi();
var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && context.Request.Path.StartsWithSegments("/api"))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await Results.Problem("Refresh the page and try again.", statusCode: 400, title: "Invalid CSRF token").ExecuteAsync(context); return; }
    }
    try { await next(); }
    catch (ArgumentException ex) when (!context.Response.HasStarted) { await Results.Problem(ex.Message, statusCode: 400, title: "Invalid request").ExecuteAsync(context); }
});
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseDefaultFiles(); app.UseStaticFiles();
app.UseAuthorization(); app.UseRateLimiter();
ApiEndpoints.Map(app.MapGroup("/api"));
ApiEndpoints.Map(app.MapGroup("/api/v1"));
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapFallbackToFile("index.html");
await SeedData.InitializeAsync(app.Services, app.Configuration);
await app.RunAsync();
public partial class Program { }
