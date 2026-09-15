using FxRates.Application.Errors;
using FxRates.Application.Events;
using FxRates.Application.Providers;
using FxRates.Application.Rates;
using FxRates.Application.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FxRates.UnitTests.Application;

public sealed class RateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly MemoryStore store = new();
    private readonly StubProvider provider = new();
    private readonly RecordingPublisher publisher = new();
    private readonly RateService service;

    public RateServiceTests() =>
        service = new RateService(store, provider, publisher, new FixedClock(), NullLogger<RateService>.Instance);

    [Fact]
    public async Task Stored_rate_is_returned_without_provider_call_even_when_old()
    {
        var rate = new Rate("USD", "EUR", 0.9m, 0.91m, RateSources.Manual, Now.AddYears(-3), null);
        store.Seed(rate);

        Assert.Equal(rate, await service.GetAsync("usd", " eur ", default));
        Assert.Equal(0, provider.Calls);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Missing_rate_is_fetched_saved_and_published_once()
    {
        var rate = await service.GetAsync("USD", "EUR", default);

        Assert.Equal(rate, store.Get("USD", "EUR"));
        Assert.Equal(RateSources.AlphaVantage, rate.Source);
        Assert.Equal(Now, rate.UpdatedAt);
        Assert.Equal(provider.Quote.QuotedAt, rate.ProviderQuotedAt);
        Assert.Equal(rate, await service.GetAsync("USD", "EUR", default));
        Assert.Equal(1, provider.Calls);
        var published = Assert.Single(publisher.Published);
        Assert.Equal(rate, published.Rate);
        Assert.Equal(Now, published.OccurredAt);
    }

    [Fact]
    public async Task Provider_timestamp_is_normalized_to_database_precision()
    {
        provider.Quote = new ProviderRate(1m, 2m, RateSources.Fake, Now.AddTicks(7));

        var rate = await service.GetAsync("USD", "EUR", default);

        Assert.Equal(Now, rate.ProviderQuotedAt);
        Assert.Equal(rate, store.Get("USD", "EUR"));
    }

    [Fact]
    public async Task Concurrent_insert_returns_stored_winner_and_does_not_publish()
    {
        var winner = new Rate("USD", "EUR", 2m, 3m, RateSources.Manual, Now, null);
        store.InsertBeforeNextAdd = winner;

        Assert.Equal(winner, await service.GetAsync("USD", "EUR", default));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Lost_insert_race_with_vanished_winner_reports_concurrent_change()
    {
        store.RejectNextAdd = true;

        var exception = await Assert.ThrowsAsync<FxException>(() => service.GetAsync("USD", "EUR", default));

        Assert.Equal(ErrorKind.ConcurrentChange, exception.Kind);
        Assert.Empty(publisher.Published);
    }

    [Theory]
    [InlineData("EUR", "EUR")]
    [InlineData("BTC", "USD")]
    [InlineData("", "USD")]
    [InlineData(null, "USD")]
    public async Task Invalid_pairs_never_call_provider(string? baseCurrency, string quoteCurrency)
    {
        var exception = await Assert.ThrowsAsync<FxException>(() => service.GetAsync(baseCurrency, quoteCurrency, default));

        Assert.Equal(ErrorKind.Validation, exception.Kind);
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("-1", "1")]
    [InlineData("2", "1")]
    [InlineData("0.123456789", "1")]
    [InlineData("1", "10000000000")]
    public async Task Invalid_manual_prices_are_not_saved(string bid, string ask)
    {
        var input = new RateInput("USD", "EUR", Price(bid), Price(ask));

        var exception = await Assert.ThrowsAsync<FxException>(() => service.CreateAsync(input, default));

        Assert.Equal(ErrorKind.Validation, exception.Kind);
        Assert.Null(store.Get("USD", "EUR"));
    }

    [Fact]
    public async Task Manual_create_normalizes_pair_and_duplicate_reports_already_exists()
    {
        var input = new RateInput("usd", "eur", 1m, 1m);

        var rate = await service.CreateAsync(input, default);
        var duplicate = await Assert.ThrowsAsync<FxException>(() => service.CreateAsync(input, default));

        Assert.Equal("USD", rate.BaseCurrency);
        Assert.Equal(RateSources.Manual, rate.Source);
        Assert.Null(rate.ProviderQuotedAt);
        Assert.Equal(ErrorKind.AlreadyExists, duplicate.Kind);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task Manual_update_clears_provider_timestamp_and_does_not_publish_created_event()
    {
        await service.GetAsync("USD", "EUR", default);

        var rate = await service.UpdateAsync(new RateInput("USD", "EUR", 2, 3), default);

        Assert.Equal(RateSources.Manual, rate.Source);
        Assert.Null(rate.ProviderQuotedAt);
        Assert.Equal(2, rate.Bid);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task Missing_update_and_delete_return_not_found()
    {
        var update = await Assert.ThrowsAsync<FxException>(() => service.UpdateAsync(new RateInput("USD", "EUR", 1, 2), default));
        var delete = await Assert.ThrowsAsync<FxException>(() => service.DeleteAsync("USD", "EUR", default));

        Assert.Equal(ErrorKind.NotFound, update.Kind);
        Assert.Equal(ErrorKind.NotFound, delete.Kind);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Deleted_rate_can_be_fetched_again()
    {
        await service.GetAsync("USD", "EUR", default);
        await service.DeleteAsync("USD", "EUR", default);
        Assert.Null(store.Get("USD", "EUR"));

        await service.GetAsync("USD", "EUR", default);

        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, publisher.Published.Count);
    }

    [Fact]
    public async Task Opposite_pairs_are_stored_separately()
    {
        await service.CreateAsync(new RateInput("USD", "EUR", 1m, 2m), default);
        await service.CreateAsync(new RateInput("EUR", "USD", 3m, 4m), default);

        Assert.Equal(2, (await service.ListAsync(default)).Count);
        Assert.Equal(3m, store.Get("EUR", "USD")!.Bid);
    }

    [Fact]
    public async Task Provider_failure_saves_and_publishes_nothing()
    {
        provider.Failure = new FxException(ErrorKind.ProviderTimeout, "Timed out");

        var exception = await Assert.ThrowsAsync<FxException>(() => service.GetAsync("USD", "EUR", default));

        Assert.Equal(ErrorKind.ProviderTimeout, exception.Kind);
        Assert.Null(store.Get("USD", "EUR"));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Invalid_provider_prices_are_rejected_as_provider_failure()
    {
        provider.Quote = new ProviderRate(0, 1, RateSources.AlphaVantage, Now);

        var exception = await Assert.ThrowsAsync<FxException>(() => service.GetAsync("USD", "EUR", default));

        Assert.Equal(ErrorKind.ProviderFailure, exception.Kind);
        Assert.Null(store.Get("USD", "EUR"));
    }

    [Fact]
    public async Task Publisher_failure_does_not_report_committed_create_as_failed()
    {
        publisher.Failure = new InvalidOperationException("Broker down");

        var rate = await service.CreateAsync(new RateInput("USD", "EUR", 1, 2), default);

        Assert.Equal(rate, store.Get("USD", "EUR"));
    }

    [Fact]
    public async Task Caller_cancellation_during_publish_is_not_swallowed()
    {
        using var cancellation = new CancellationTokenSource();
        publisher.OnPublish = () => cancellation.Cancel();
        publisher.Failure = new OperationCanceledException(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateAsync(new RateInput("USD", "EUR", 1, 2), cancellation.Token));

        Assert.NotNull(store.Get("USD", "EUR"));
    }

    private static decimal Price(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubProvider : IExchangeRateProvider
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public ProviderRate Quote { get; set; } = new(0.9m, 0.91m, RateSources.AlphaVantage, Now.AddHours(-1));

        public Task<ProviderRate> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                throw Failure;
            }
            return Task.FromResult(Quote);
        }
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<RateCreated> Published { get; } = [];
        public Exception? Failure { get; set; }
        public Action? OnPublish { get; set; }

        public Task PublishAsync(RateCreated message, CancellationToken cancellationToken)
        {
            OnPublish?.Invoke();
            if (Failure is not null)
            {
                throw Failure;
            }
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>Keyed by directed pair, like the real table. Two knobs simulate losing an insert race.</summary>
    private sealed class MemoryStore : IRateStore
    {
        private readonly Dictionary<(string Base, string Quote), Rate> rates = [];

        /// <summary>Inserted just before the next add, so that add fails and the lookup finds this row.</summary>
        public Rate? InsertBeforeNextAdd { get; set; }

        /// <summary>The next add fails and nothing is inserted, as if the winner was deleted straight after.</summary>
        public bool RejectNextAdd { get; set; }

        public void Seed(Rate rate) => rates[(rate.BaseCurrency, rate.QuoteCurrency)] = rate;

        public Rate? Get(string baseCurrency, string quoteCurrency) => rates.GetValueOrDefault((baseCurrency, quoteCurrency));

        public Task<IReadOnlyList<Rate>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Rate>>(rates.Values.OrderBy(x => x.BaseCurrency).ThenBy(x => x.QuoteCurrency).ToList());

        public Task<Rate?> FindAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken) =>
            Task.FromResult(Get(baseCurrency, quoteCurrency));

        public Task<bool> TryAddAsync(Rate rate, CancellationToken cancellationToken)
        {
            if (RejectNextAdd)
            {
                RejectNextAdd = false;
                return Task.FromResult(false);
            }
            if (InsertBeforeNextAdd is { } winner)
            {
                InsertBeforeNextAdd = null;
                Seed(winner);
            }
            return Task.FromResult(rates.TryAdd((rate.BaseCurrency, rate.QuoteCurrency), rate));
        }

        public Task<bool> UpdateAsync(Rate rate, CancellationToken cancellationToken)
        {
            var key = (rate.BaseCurrency, rate.QuoteCurrency);
            if (!rates.ContainsKey(key))
            {
                return Task.FromResult(false);
            }
            rates[key] = rate;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken) =>
            Task.FromResult(rates.Remove((baseCurrency, quoteCurrency)));
    }
}
