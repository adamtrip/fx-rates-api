using FxRates.Application.Events;
using Microsoft.Extensions.Logging;

namespace FxRates.Infrastructure.Messaging;

/// <summary>Logs explicitly identify undelivered events so local logging cannot be mistaken for queue delivery.</summary>
public sealed class LoggingEventPublisher(ILogger<LoggingEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync(RateCreated message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Event {EventType} {EventId}: rate created for {Base}/{Quote}. Logging adapter only; no broker delivery",
            message.EventType, message.EventId, message.Rate.BaseCurrency, message.Rate.QuoteCurrency);
        return Task.CompletedTask;
    }
}
