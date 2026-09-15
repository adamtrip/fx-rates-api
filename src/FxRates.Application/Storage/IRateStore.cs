using FxRates.Application.Rates;

namespace FxRates.Application.Storage;

/// <summary>Callers pass normalized upper-case currency codes. Expected conflicts and misses return booleans.</summary>
public interface IRateStore
{
    /// <summary>Ordered by base then quote currency.</summary>
    Task<IReadOnlyList<Rate>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The stored rate for the pair, or null when none exists.</summary>
    Task<Rate?> FindAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken);

    /// <summary>Returns false if the pair already exists, including when a concurrent insert wins.</summary>
    Task<bool> TryAddAsync(Rate rate, CancellationToken cancellationToken);

    /// <summary>Replaces every stored value for the pair. Returns false when no row exists.</summary>
    Task<bool> UpdateAsync(Rate rate, CancellationToken cancellationToken);

    /// <summary>Removes the pair. Returns false when no row exists.</summary>
    Task<bool> DeleteAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken);
}
