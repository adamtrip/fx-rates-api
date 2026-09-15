namespace FxRates.Api.RateLimiting;

/// <summary>All callers share one fixed window per instance. Replicas do not share the budget.</summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>When false, the limiter is registered but no endpoint uses it.</summary>
    public bool Enabled { get; set; } = true;

    public int PermitLimit { get; set; } = 60;

    public int WindowSeconds { get; set; } = 60;
}
