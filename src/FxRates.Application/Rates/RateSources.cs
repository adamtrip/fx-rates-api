namespace FxRates.Application.Rates;

/// <summary>Values stored in <see cref="Rate.Source"/>. Kept short because the column is varchar(32).</summary>
public static class RateSources
{
    public const string Manual = "Manual";

    public const string AlphaVantage = "AlphaVantage";

    public const string Fake = "Fake";
}
