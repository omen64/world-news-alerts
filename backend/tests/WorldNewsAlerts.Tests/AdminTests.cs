using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorldNewsAlerts.Infrastructure;
using WorldNewsAlerts.Infrastructure.Persistence;

namespace WorldNewsAlerts.Tests;

public class AdminTests
{
    [Fact]
    public async Task AdminCanManageSettingsInspectUsersAndRetryJobsWithoutSeeingSecrets()
    {
        using var app = new AppFactory(); using var client = app.Client();
        var id = await TestAuth.LoginAsync(client, "admin@example.com");
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(id))!, "Admin")).Succeeded);
        }
        await client.SendAsync(HttpMethod.Post, "/api/auth/logout");
        await client.SendAsync(HttpMethod.Post, "/api/auth/login", new { email = "admin@example.com", password = TestAuth.Password });
        var usersResponse = await client.GetAsync("/api/admin/users"); Assert.Equal(HttpStatusCode.OK, usersResponse.StatusCode);
        var usersJson = await usersResponse.Content.ReadAsStringAsync(); Assert.DoesNotContain("passwordHash", usersJson, StringComparison.OrdinalIgnoreCase);
        var options = (await client.GetFromJsonAsync<RuntimeSettings>("/api/admin/settings"))!;
        options.EmailProvider = "resend"; options.RetrievalIntervalMinutes = 15;
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Put, "/api/admin/settings", options)).StatusCode);
        Assert.Equal("resend", (await client.GetFromJsonAsync<RuntimeSettings>("/api/admin/settings"))!.EmailProvider);
        var channels = await client.GetFromJsonAsync<JsonElement>("/api/admin/channels");
        Assert.Contains(channels.GetProperty("emailProviders").EnumerateArray(), p => p.GetProperty("key").GetString() == "resend");
        var providers = await client.GetAsync("/api/admin/providers"); Assert.Equal(HttpStatusCode.OK, providers.StatusCode);
        var retrieval = await client.SendAsync(HttpMethod.Post, "/api/admin/jobs/retrieve-news"); Assert.Equal(HttpStatusCode.OK, retrieval.StatusCode);
        Assert.Equal("Succeeded", (await retrieval.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Post, "/api/admin/channels/test-email")).StatusCode);
        app.Email.Fail = true;
        await client.SendAsync(HttpMethod.Post, "/api/admin/jobs/dispatch");
        Guid jobId;
        using (var scope = app.Services.CreateScope()) jobId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeliveryJobs.Where(j => j.MessageType == "test").Select(j => j.Id).SingleAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Post, $"/api/admin/jobs/{jobId}/retry")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(HttpMethod.Put, "/api/admin/users/" + id, new { isDisabled = true })).StatusCode);
    }
}
