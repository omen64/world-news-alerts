using WorldNewsAlerts.Application;
using WorldNewsAlerts.Domain;
using WorldNewsAlerts.Infrastructure.Services;

namespace WorldNewsAlerts.Infrastructure.NotificationChannels;

public class SlackNotificationChannel(SlackApi api, SlackService slack) : INotificationChannel
{
    public string ChannelKey => "slack";
    public async Task<DeliveryResult> SendAsync(NotificationMessage message, NotificationDestination destination, CancellationToken cancellationToken)
    {
        var workspace = destination.SlackWorkspace ?? throw new InvalidOperationException("Slack workspace is missing.");
        var token = slack.Token(workspace); var target = destination.TargetIdentifier;
        if (destination.Kind == "slack_dm")
        {
            var dm = await api.CallAsync("conversations.open", token, new { users = target }, cancellationToken);
            target = dm.GetProperty("channel").GetProperty("id").GetString()!;
        }
        // Plain text disables unwanted @mentions and automatic link unfurling.
        var result = await api.CallAsync("chat.postMessage", token, new { channel = target, text = message.Text,
            mrkdwn = false, parse = "none", unfurl_links = false, unfurl_media = false, client_msg_id = message.Id.ToString() }, cancellationToken);
        return new DeliveryResult(result.GetProperty("ts").GetString());
    }
}
