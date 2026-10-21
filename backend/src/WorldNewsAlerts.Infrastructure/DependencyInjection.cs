using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorldNewsAlerts.Application;
using WorldNewsAlerts.Infrastructure.BackgroundJobs;
using WorldNewsAlerts.Infrastructure.NewsProviders;
using WorldNewsAlerts.Infrastructure.NotificationChannels;
using WorldNewsAlerts.Infrastructure.Persistence;
using WorldNewsAlerts.Infrastructure.Services;

namespace WorldNewsAlerts.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(config.GetConnectionString("DefaultConnection") ?? "Data Source=App_Data/news.db;Default Timeout=30",
            sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
        {
            o.User.RequireUniqueEmail = true; o.SignIn.RequireConfirmedEmail = true;
            o.Password.RequiredLength = 10; o.Lockout.MaxFailedAccessAttempts = 5;
        }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services.AddSingleton<OperationGates>(); services.AddSingleton<DeliverySignal>();
        services.AddScoped<SettingsStore>(); services.AddScoped<NewsService>(); services.AddScoped<OutboxService>();
        services.AddScoped<SubscriptionService>(); services.AddScoped<NotificationDispatcher>();
        services.AddScoped<INewsProvider, FakeNewsProvider>(); services.AddScoped<INewsProvider, CodexNewsProvider>();
        services.AddScoped<INotificationChannel, EmailNotificationChannel>(); services.AddScoped<INotificationChannel, SlackNotificationChannel>();
        services.AddScoped<IEmailProvider, SmtpEmailProvider>(); services.AddScoped<IEmailProvider, DevelopmentEmailProvider>();
        services.AddHttpClient<ResendEmailProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IEmailProvider>(sp => sp.GetRequiredService<ResendEmailProvider>());
        services.AddHttpClient<SlackApi>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<SlackService>(c => c.Timeout = TimeSpan.FromSeconds(30));
        if (config.GetValue("Workers:Enabled", true))
        { services.AddHostedService<NewsRetrievalWorker>(); services.AddHostedService<NotificationDeliveryWorker>(); }
        return services;
    }
}
