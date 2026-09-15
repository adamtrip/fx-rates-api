namespace FxRates.Infrastructure.Persistence;

/// <summary>Separate from the application record to keep persistence attributes out of the domain.</summary>
public sealed class RateEntity
{
    public required string BaseCurrency { get; set; }
    public required string QuoteCurrency { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ProviderQuotedAt { get; set; }
}
