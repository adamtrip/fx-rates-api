using FxRates.Application.Rates;

namespace FxRates.Application.Events;

/// <summary>Raised per successful insert, including recreation after deletion. Updates and lost insert races do not raise it.</summary>
/// <param name="EventId">Unique per event. Consumers can use it to drop duplicates.</param>
/// <param name="OccurredAt">When the application raised the event, just after the row was committed.</param>
/// <param name="Rate">The stored rate exactly as the API returned it.</param>
public sealed record RateCreated(Guid EventId, DateTimeOffset OccurredAt, Rate Rate)
{
    /// <summary>Message type and routing key. Bump the version suffix when the payload shape changes.</summary>
    public string EventType => "rate.created.v1";
}
