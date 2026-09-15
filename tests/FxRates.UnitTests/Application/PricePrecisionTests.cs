using System.Globalization;
using System.Text.Json;
using FxRates.Application.Rates;
using Xunit;

namespace FxRates.UnitTests.Application;

public sealed class PricePrecisionTests
{
    [Theory]
    [InlineData("0.91000000", "0.91")]
    [InlineData("199.800", "199.8")]
    [InlineData("1.00000000", "1")]
    [InlineData("0.91238513", "0.91238513")]
    [InlineData("9999999999.99999999", "9999999999.99999999")]
    public void Normalizes_prices_without_changing_the_value(string input, string expectedJson)
    {
        var value = decimal.Parse(input, CultureInfo.InvariantCulture);

        var normalized = PricePrecision.Normalize(value);
        var rate = new Rate("USD", "EUR", value, value, RateSources.Manual, DateTimeOffset.UnixEpoch, null);

        Assert.Equal(value, normalized);
        Assert.Equal(expectedJson, JsonSerializer.Serialize(normalized));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(rate, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(value, rate.Bid);
        Assert.Equal(value, rate.Ask);
        Assert.Equal(expectedJson, json.RootElement.GetProperty("bid").GetRawText());
        Assert.Equal(expectedJson, json.RootElement.GetProperty("ask").GetRawText());
    }
}
