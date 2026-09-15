using System.Globalization;

namespace FxRates.Application.Rates;

/// <summary>Accepts plain decimal notation only, preventing excess digits from silently rounding away.</summary>
public static class PriceParser
{
    // Bound scans of zero-padded input, which can exceed the meaningful digit limits.
    private const int MaxLength = 128;

    /// <summary>Leading and trailing zeros do not count toward the digit limits.</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        // Check the digit shape before decimal.TryParse, which would silently round long fractions.
        var digits = text.AsSpan();
        if (digits[0] == '-')
        {
            digits = digits[1..];
        }
        if (digits.IsEmpty)
        {
            return false;
        }

        var dot = digits.IndexOf('.');
        var whole = dot < 0 ? digits : digits[..dot];
        var fraction = dot < 0 ? ReadOnlySpan<char>.Empty : digits[(dot + 1)..];
        if (whole.IsEmpty || (dot >= 0 && fraction.IsEmpty))
        {
            return false;
        }
        foreach (var digit in whole)
        {
            if (digit is < '0' or > '9')
            {
                return false;
            }
        }
        foreach (var digit in fraction)
        {
            if (digit is < '0' or > '9')
            {
                return false;
            }
        }
        if (whole.TrimStart('0').Length > PricePrecision.IntegerDigits ||
            fraction.TrimEnd('0').Length > PricePrecision.FractionDigits)
        {
            return false;
        }

        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }
}
