using FxRates.Application.Rates;
using Xunit;

namespace FxRates.UnitTests.Application;

public sealed class PriceParserTests
{
    [Theory]
    [InlineData("1", "1")]
    [InlineData("0.91234567", "0.91234567")]
    [InlineData("9999999999.99999999", "9999999999.99999999")]
    [InlineData("0000000000001.50000000000", "1.5")]
    [InlineData("-1.5", "-1.5")]
    public void Accepts_plain_decimals_within_precision(string text, string expected)
    {
        Assert.True(PriceParser.TryParse(text, out var value));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("+1")]
    [InlineData("1e0")]
    [InlineData("1E+0")]
    [InlineData("1,5")]
    [InlineData("0.123456789")]
    [InlineData("10000000000")]
    [InlineData("1.000000000000000000000000000001")]
    public void Rejects_other_shapes_and_excess_digits(string? text) => Assert.False(PriceParser.TryParse(text, out _));

    [Fact]
    public void Rejects_input_longer_than_the_scan_limit() =>
        Assert.False(PriceParser.TryParse(new string('0', 200) + "1", out _));

    [Fact]
    public void Maximum_matches_the_digit_limits() =>
        Assert.Equal(9_999_999_999.99999999m, PricePrecision.Maximum);
}
