using System.ComponentModel;
using FxRates.Application.Rates;

namespace FxRates.Api.Contracts;

public sealed record RateResponse(
    [property: Description("Upper-case ISO 4217 code of the currency being priced.")]
    string BaseCurrency,
    [property: Description("Upper-case ISO 4217 code the prices are expressed in.")]
    string QuoteCurrency,
    [property: Description("Buy price for one unit of the base currency.")]
    decimal Bid,
    [property: Description("Sell price for one unit of the base currency. Never below bid.")]
    decimal Ask,
    [property: Description("Where the prices came from: Manual, AlphaVantage, or Fake.")]
    string Source,
    [property: Description("When the application stored the current values. Not a freshness guarantee.")]
    DateTimeOffset UpdatedAt,
    [property: Description("The provider's own quote time. Null after a manual create or update.")]
    DateTimeOffset? ProviderQuotedAt)
{
    public static RateResponse From(Rate rate) =>
        new(rate.BaseCurrency, rate.QuoteCurrency, rate.Bid, rate.Ask, rate.Source, rate.UpdatedAt, rate.ProviderQuotedAt);
}
