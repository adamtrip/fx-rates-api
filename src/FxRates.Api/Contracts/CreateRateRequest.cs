using System.ComponentModel;
using System.Text.Json.Serialization;
using FxRates.Application.Rates;

namespace FxRates.Api.Contracts;

public sealed record CreateRateRequest(
    [property: Description("ISO 4217 code of the currency being priced, for example USD. Case-insensitive.")]
    string? BaseCurrency,
    [property: Description("Code the prices are expressed in, for example EUR. Case-insensitive; must differ from baseCurrency.")]
    string? QuoteCurrency,
    [property: Description("Buy price. Positive, plain decimal notation, at most 10 integer and 8 fractional digits.")]
    [property: JsonConverter(typeof(PriceJsonConverter))]
    decimal Bid,
    [property: Description("Sell price. Same format as bid and never below it.")]
    [property: JsonConverter(typeof(PriceJsonConverter))]
    decimal Ask)
{
    public RateInput ToInput() => new(BaseCurrency, QuoteCurrency, Bid, Ask);
}
