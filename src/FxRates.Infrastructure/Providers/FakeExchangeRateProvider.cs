using FxRates.Application.Providers;
using FxRates.Application.Rates;

namespace FxRates.Infrastructure.Providers;

/// <summary>Synthetic demo quotes, never market data. Requires explicit selection; real requests never fall back here.</summary>
public sealed class FakeExchangeRateProvider(TimeProvider clock) : IExchangeRateProvider
{
    // Rough units of each currency per one US dollar. Any pair is derived from these two ratios.
    private static readonly Dictionary<string, decimal> UnitsPerUsd = new()
    {
        ["USD"] = 1m,
        ["EUR"] = 0.90m,
        ["GBP"] = 0.75m,
        ["JPY"] = 150m,
        ["CHF"] = 0.85m,
        ["CAD"] = 1.35m,
        ["AUD"] = 1.50m,
        ["NZD"] = 1.65m
    };

    public Task<ProviderRate> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mid = UnitsPerUsd[quoteCurrency] / UnitsPerUsd[baseCurrency];
        // A symmetric 0.1% spread around the mid price keeps bid below ask for every pair.
        var bid = decimal.Round(mid * 0.999m, PricePrecision.FractionDigits);
        var ask = decimal.Round(mid * 1.001m, PricePrecision.FractionDigits);
        return Task.FromResult(new ProviderRate(bid, ask, RateSources.Fake, clock.GetUtcNow()));
    }
}
