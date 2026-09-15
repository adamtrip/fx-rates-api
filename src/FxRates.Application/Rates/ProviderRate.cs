namespace FxRates.Application.Rates;

/// <param name="QuotedAt">The provider's quote time, or null when the provider does not report one.</param>
public sealed record ProviderRate(decimal Bid, decimal Ask, string Source, DateTimeOffset? QuotedAt);
