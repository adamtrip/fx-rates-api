namespace FxRates.Application.Events;

/// <summary>Called after commit. A publishing failure loses the event, not the stored rate; there is no outbox.</summary>
public interface IEventPublisher
{
    Task PublishAsync(RateCreated message, CancellationToken cancellationToken);
}
