namespace WorldNewsAlerts.Domain;

public static class Catalog
{
    public static readonly IReadOnlyDictionary<string, string> Categories = new Dictionary<string, string>
    {
        ["breaking_news"] = "Breaking news", ["market_movements"] = "Market movements",
        ["natural_disasters"] = "Natural disasters", ["geopolitics"] = "Geopolitics",
        ["technology_ai"] = "Technology & AI", ["economy"] = "Economy", ["health"] = "Health"
    };
    public static readonly IReadOnlyDictionary<string, int> Periods = new Dictionary<string, int>
    { ["6h"] = 6, ["24h"] = 24, ["3d"] = 72, ["7d"] = 168 };
}

public class Category { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }
public class NewsEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Importance { get; set; } = "significant";
    public string? Location { get; set; }
    public DateTime? EventTimeUtc { get; set; }
    public DateTime RetrievedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime EffectiveAtUtc { get; set; }
    public string EventFingerprint { get; set; } = "";
    public string ProviderKey { get; set; } = "";
    public int Revision { get; set; } = 1;
    public List<NewsEventCategory> Categories { get; set; } = [];
    public List<NewsSource> Sources { get; set; } = [];
}
public class NewsEventCategory
{
    public Guid NewsEventId { get; set; }
    public string CategoryId { get; set; } = "";
}
public class NewsSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NewsEventId { get; set; }
    public string Publisher { get; set; } = "";
    public string Url { get; set; } = "";
    public string NormalizedUrl { get; set; } = "";
    public DateTime? PublishedAtUtc { get; set; }
}
public class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string MinimumImportance { get; set; } = "significant";
    public bool IsActive { get; set; } = true;
    public bool NotifyUpdates { get; set; }
    public int InitialDigestLookbackHours { get; set; } = 24;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? InitializedAtUtc { get; set; }
    public List<SubscriptionCategory> Categories { get; set; } = [];
    public List<SubscriptionDestination> Destinations { get; set; } = [];
}
public class SubscriptionCategory { public Guid SubscriptionId { get; set; } public string CategoryId { get; set; } = ""; }
public class SubscriptionDestination { public Guid SubscriptionId { get; set; } public Guid DestinationId { get; set; } public NotificationDestination Destination { get; set; } = null!; }
public class NotificationDestination
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? UserId { get; set; }
    public string ChannelKey { get; set; } = "email";
    public string Kind { get; set; } = "email";
    public string TargetIdentifier { get; set; } = "";
    public string TargetKey { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string? PublicCategory { get; set; }
    public Guid? SlackWorkspaceId { get; set; }
    public SlackWorkspace? SlackWorkspace { get; set; }
}
public class SlackWorkspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TeamId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProtectedBotToken { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public class SlackConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public Guid SlackWorkspaceId { get; set; }
    public SlackWorkspace Workspace { get; set; } = null!;
    public string SlackUserId { get; set; } = "";
}
public class OAuthState
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}
public class DeliveryJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DestinationId { get; set; }
    public NotificationDestination Destination { get; set; } = null!;
    public string? UserId { get; set; }
    public string MessageType { get; set; } = "news";
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string TextBody { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseUntilUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? ExternalMessageId { get; set; }
    public string? Error { get; set; }
    public List<DeliveryEvent> Events { get; set; } = [];
}
public class DeliveryEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NewsEventId { get; set; }
    public Guid DestinationId { get; set; }
    public int EventRevision { get; set; }
    public Guid DeliveryJobId { get; set; }
    public DeliveryJob DeliveryJob { get; set; } = null!;
}
public class NewsProviderExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProviderKey { get; set; } = "";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = "Running";
    public int EventsCount { get; set; }
    public int InsertedCount { get; set; }
    public string? Error { get; set; }
}
public class SystemSetting { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }
