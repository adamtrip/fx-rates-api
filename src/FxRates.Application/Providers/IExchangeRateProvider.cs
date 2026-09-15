using FxRates.Application.Errors;
using FxRates.Application.Rates;

namespace FxRates.Application.Providers;

/// <summary>Translate transport and payload problems into <see cref="FxException"/> with a provider <see cref="ErrorKind"/>.</summary>
public interface IExchangeRateProvider
{
    /// <summary>Never substitutes synthetic data for a failed request.</summary>
    Task<ProviderRate> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken);
}
