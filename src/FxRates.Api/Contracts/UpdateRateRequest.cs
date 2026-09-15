using System.ComponentModel;
using System.Text.Json.Serialization;
using FxRates.Application.Rates;

namespace FxRates.Api.Contracts;

/// <summary>The currency pair comes from the route.</summary>
public sealed record UpdateRateRequest(
    [property: Description("Buy price. Positive, plain decimal notation, at most 10 integer and 8 fractional digits.")]
    [property: JsonConverter(typeof(PriceJsonConverter))]
    decimal Bid,
    [property: Description("Sell price. Same format as bid and never below it.")]
    [property: JsonConverter(typeof(PriceJsonConverter))]
    decimal Ask)
{
    public RateInput ToInput(string baseCurrency, string quoteCurrency) => new(baseCurrency, quoteCurrency, Bid, Ask);
}
