using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MimeKit;
using WorldNewsAlerts.Application;
using WorldNewsAlerts.Domain;

namespace WorldNewsAlerts.Infrastructure.NotificationChannels;

public class EmailNotificationChannel(IEnumerable<IEmailProvider> providers, SettingsStore settings) : INotificationChannel
{
    public string ChannelKey => "email";
    public async Task<DeliveryResult> SendAsync(NotificationMessage message, NotificationDestination destination, CancellationToken cancellationToken)
    {
        var key = (await settings.GetAsync(cancellationToken)).EmailProvider;
        var provider = providers.SingleOrDefault(p => p.ProviderKey == key) ?? throw new InvalidOperationException("Unknown email provider.");
        return await provider.SendAsync(message, destination.TargetIdentifier, cancellationToken);
    }
}
public class ResendEmailProvider(HttpClient http, IConfiguration config) : IEmailProvider
{
    public string ProviderKey => "resend";
    public async Task<DeliveryResult> SendAsync(NotificationMessage message, string recipient, CancellationToken cancellationToken)
    {
        var key = config["Email:Resend:ApiKey"];
        var from = config["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(from)) throw new InvalidOperationException("Resend API key and FromAddress must be configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Add("Idempotency-Key", $"world-news/{message.Id:N}");
        request.Content = JsonContent.Create(new { from, to = new[] { recipient }, subject = message.Subject, html = message.Html, text = message.Text });
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Resend returned HTTP {(int)response.StatusCode}.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return new DeliveryResult(json.RootElement.GetProperty("id").GetString());
    }
}
public class SmtpEmailProvider(IConfiguration config) : IEmailProvider
{
    public string ProviderKey => "smtp";
    public async Task<DeliveryResult> SendAsync(NotificationMessage message, string recipient, CancellationToken cancellationToken)
    {
        var host = config["Email:Smtp:Host"] ?? throw new InvalidOperationException("SMTP host must be configured.");
        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse(config["Email:FromAddress"] ?? throw new InvalidOperationException("FromAddress must be configured.")));
        email.To.Add(MailboxAddress.Parse(recipient));
        email.Subject = message.Subject;
        email.MessageId = $"{message.Id:N}@world-news-alerts";
        email.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();
        using var client = new SmtpClient { Timeout = 30000 };
        var security = Enum.Parse<SecureSocketOptions>(config["Email:Smtp:Security"] ?? "StartTls", ignoreCase: true);
        await client.ConnectAsync(host, config.GetValue("Email:Smtp:Port", 587), security, cancellationToken);
        if (!string.IsNullOrEmpty(config["Email:Smtp:Username"]))
            await client.AuthenticateAsync(config["Email:Smtp:Username"]!, config["Email:Smtp:Password"] ?? "", cancellationToken);
        var id = await client.SendAsync(email, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        return new DeliveryResult(id);
    }
}
public class DevelopmentEmailProvider(IHostEnvironment env) : IEmailProvider
{
    public string ProviderKey => "development";
    public async Task<DeliveryResult> SendAsync(NotificationMessage message, string recipient, CancellationToken cancellationToken)
    {
        if (!env.IsDevelopment() && !env.IsEnvironment("Testing")) throw new InvalidOperationException("Development email is unavailable outside development.");
        var folder = Path.Combine(env.ContentRootPath, "App_Data", "mail");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, $"{message.Id:N}.html"), message.Html, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder, $"{message.Id:N}.txt"), $"To: {recipient}\nSubject: {message.Subject}\n\n{message.Text}", cancellationToken);
        return new DeliveryResult($"development/{message.Id:N}");
    }
}
