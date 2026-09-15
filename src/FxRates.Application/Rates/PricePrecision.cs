using System.Globalization;

namespace FxRates.Application.Rates;

/// <summary>Shared with the PostgreSQL column precision. Changing these limits requires an EF Core migration.</summary>
public static class PricePrecision
{
    public const int IntegerDigits = 10;

    /// <summary>Extra fractional digits are rejected, never rounded.</summary>
    public const int FractionDigits = 8;

    public const int TotalDigits = IntegerDigits + FractionDigits;

    public static decimal Maximum { get; } = decimal.Parse(
        new string('9', IntegerDigits) + "." + new string('9', FractionDigits),
        CultureInfo.InvariantCulture);

    // Division by a scaled one removes fractional trailing zeros without rounding the value.
    public static decimal Normalize(decimal value) => value / 1.000000000000000000000000000m;
}
