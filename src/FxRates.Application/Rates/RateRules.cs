using FxRates.Application.Errors;

namespace FxRates.Application.Rates;

public static class RateRules
{
    public static IReadOnlyList<string> SupportedCurrencies { get; } =
        Array.AsReadOnly(["EUR", "USD", "GBP", "JPY", "CHF", "CAD", "AUD", "NZD"]);

    private static readonly HashSet<string> SupportedLookup = new(SupportedCurrencies, StringComparer.Ordinal);

    public static (string Base, string Quote) ValidatePair(string? baseCurrency, string? quoteCurrency)
    {
        var normalizedBase = baseCurrency?.Trim().ToUpperInvariant();
        var normalizedQuote = quoteCurrency?.Trim().ToUpperInvariant();
        if (normalizedBase is null || normalizedQuote is null ||
            !SupportedLookup.Contains(normalizedBase) || !SupportedLookup.Contains(normalizedQuote))
        {
            throw new FxException(ErrorKind.Validation, "Both currencies must belong to the supported currency list.");
        }
        if (normalizedBase == normalizedQuote)
        {
            throw new FxException(ErrorKind.Validation, "Base and quote currencies must differ.");
        }
        return (normalizedBase, normalizedQuote);
    }

    /// <summary>Returns null for valid prices or a caller-safe reason. Providers report rejection as a provider failure.</summary>
    public static string? CheckPrices(decimal bid, decimal ask)
    {
        if (bid <= 0 || ask <= 0 || bid > ask)
        {
            return "Prices must be positive and bid must not exceed ask.";
        }
        if (bid > PricePrecision.Maximum || ask > PricePrecision.Maximum ||
            decimal.Round(bid, PricePrecision.FractionDigits) != bid ||
            decimal.Round(ask, PricePrecision.FractionDigits) != ask)
        {
            return $"Prices support up to {PricePrecision.IntegerDigits} integer " +
                $"and {PricePrecision.FractionDigits} fractional digits.";
        }
        return null;
    }

    public static void ValidatePrices(decimal bid, decimal ask)
    {
        if (CheckPrices(bid, ask) is { } reason)
        {
            throw new FxException(ErrorKind.Validation, reason);
        }
    }
}
