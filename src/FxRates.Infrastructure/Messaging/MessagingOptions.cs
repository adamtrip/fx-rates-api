namespace FxRates.Infrastructure.Messaging;

/// <summary>Separate connection fields avoid URL-encoding passwords.</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    public EventPublisherMode Mode { get; set; } = EventPublisherMode.Logging;

    public string Host { get; set; } = "";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "";

    public string Password { get; set; } = "";

    public string VirtualHost { get; set; } = "/";

    public string Exchange { get; set; } = "fx-rates.events";

    public string Queue { get; set; } = "fx-rates.rate-created";
}
