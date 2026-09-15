namespace FxRates.Infrastructure.Providers;

public sealed class ExchangeProviderOptions
{
    public const string SectionName = "ExchangeProvider";

    public ExchangeProviderMode Mode { get; set; } = ExchangeProviderMode.AlphaVantage;

    public string ApiKey { get; set; } = "";

    /// <summary>Whole-request timeout for one provider call, between 1 and 60 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
