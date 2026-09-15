namespace FxRates.Application.Rates;

/// <summary>Currency codes are raw; validation trims and upper-cases them.</summary>
public sealed record RateInput(string? BaseCurrency, string? QuoteCurrency, decimal Bid, decimal Ask);
