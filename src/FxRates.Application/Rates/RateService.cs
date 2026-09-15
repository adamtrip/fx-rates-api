using FxRates.Application.Errors;
using FxRates.Application.Events;
using FxRates.Application.Providers;
using FxRates.Application.Storage;
using Microsoft.Extensions.Logging;

namespace FxRates.Application.Rates;

public sealed class RateService(
    IRateStore store,
    IExchangeRateProvider provider,
    IEventPublisher publisher,
    TimeProvider clock,
    ILogger<RateService> logger)
{
    /// <summary>Never contacts the provider.</summary>
    public Task<IReadOnlyList<Rate>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    /// <summary>Returns stored prices regardless of age; fetches and stores a provider quote on a miss.</summary>
    public async Task<Rate> GetAsync(string? baseCurrency, string? quoteCurrency, CancellationToken cancellationToken)
    {
        var pair = RateRules.ValidatePair(baseCurrency, quoteCurrency);
        var existing = await store.FindAsync(pair.Base, pair.Quote, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var quote = await provider.GetAsync(pair.Base, pair.Quote, cancellationToken);
        // Recheck pluggable providers so invalid quotes cannot bypass the manual price rules.
        if (RateRules.CheckPrices(quote.Bid, quote.Ask) is not null)
        {
            throw new FxException(ErrorKind.ProviderFailure, "The provider returned invalid prices.");
        }

        var rate = new Rate(
            pair.Base,
            pair.Quote,
            quote.Bid,
            quote.Ask,
            quote.Source,
            NormalizeTimestamp(clock.GetUtcNow()),
            quote.QuotedAt is { } quotedAt ? NormalizeTimestamp(quotedAt) : null);
        if (await store.TryAddAsync(rate, cancellationToken))
        {
            await PublishCreatedAsync(rate, cancellationToken);
            return rate;
        }

        // A concurrent insert won. Return its row without raising another event.
        return await store.FindAsync(pair.Base, pair.Quote, cancellationToken)
            ?? throw new FxException(ErrorKind.ConcurrentChange,
                "The rate changed while the request was in progress. Retry the request.");
    }

    public async Task<Rate> CreateAsync(RateInput input, CancellationToken cancellationToken)
    {
        var rate = BuildManualRate(input);
        if (!await store.TryAddAsync(rate, cancellationToken))
        {
            throw new FxException(ErrorKind.AlreadyExists, "A rate already exists for this pair.");
        }
        await PublishCreatedAsync(rate, cancellationToken);
        return rate;
    }

    /// <summary>Manual updates clear the provider timestamp.</summary>
    public async Task<Rate> UpdateAsync(RateInput input, CancellationToken cancellationToken)
    {
        var rate = BuildManualRate(input);
        if (!await store.UpdateAsync(rate, cancellationToken))
        {
            throw new FxException(ErrorKind.NotFound, "No stored rate exists for this pair.");
        }
        return rate;
    }

    /// <summary>Deletion allows a later lookup to fetch the pair again.</summary>
    public async Task DeleteAsync(string? baseCurrency, string? quoteCurrency, CancellationToken cancellationToken)
    {
        var pair = RateRules.ValidatePair(baseCurrency, quoteCurrency);
        if (!await store.DeleteAsync(pair.Base, pair.Quote, cancellationToken))
        {
            throw new FxException(ErrorKind.NotFound, "No stored rate exists for this pair.");
        }
    }

    private Rate BuildManualRate(RateInput input)
    {
        var pair = RateRules.ValidatePair(input.BaseCurrency, input.QuoteCurrency);
        RateRules.ValidatePrices(input.Bid, input.Ask);
        return new Rate(pair.Base, pair.Quote, input.Bid, input.Ask, RateSources.Manual,
            NormalizeTimestamp(clock.GetUtcNow()), null);
    }

    /// <summary>Truncate to PostgreSQL microseconds so write responses and later reads have equal timestamps.</summary>
    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);

    private async Task PublishCreatedAsync(Rate rate, CancellationToken cancellationToken)
    {
        var message = new RateCreated(Guid.NewGuid(), NormalizeTimestamp(clock.GetUtcNow()), rate);
        try
        {
            await publisher.PublishAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The row is committed. Log the lost event and let the request succeed.
            logger.LogWarning(exception, "Rate created for {Base}/{Quote}, but publishing event {EventId} failed",
                rate.BaseCurrency, rate.QuoteCurrency, message.EventId);
        }
    }
}
