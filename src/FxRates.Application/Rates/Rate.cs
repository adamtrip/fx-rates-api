namespace FxRates.Application.Rates;

/// <summary>USD/EUR and EUR/USD are independent directed pairs.</summary>
/// <param name="BaseCurrency">ISO 4217 code of the currency being priced, for example USD in USD/EUR.</param>
/// <param name="QuoteCurrency">ISO 4217 code the prices are expressed in, for example EUR in USD/EUR.</param>
/// <param name="Bid">Price at which the quoting party buys one unit of the base currency.</param>
/// <param name="Ask">
/// Price at which the quoting party sells one unit of the base currency. Never below <paramref name="Bid"/>.
/// </param>
/// <param name="Source">Where the prices came from. One of the <see cref="RateSources"/> values.</param>
/// <param name="UpdatedAt">When the application stored the current values. Says nothing about market freshness.</param>
/// <param name="ProviderQuotedAt">The provider's own quote time, or null when a person entered the prices.</param>
public sealed record Rate(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Bid,
    decimal Ask,
    string Source,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProviderQuotedAt)
{
    public decimal Bid { get; init; } = PricePrecision.Normalize(Bid);

    public decimal Ask { get; init; } = PricePrecision.Normalize(Ask);
}
