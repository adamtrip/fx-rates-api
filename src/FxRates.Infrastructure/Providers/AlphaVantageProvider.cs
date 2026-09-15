using System.Globalization;
using System.Net;
using System.Text.Json;
using FxRates.Application.Errors;
using FxRates.Application.Providers;
using FxRates.Application.Rates;
using Microsoft.Extensions.Options;

namespace FxRates.Infrastructure.Providers;

/// <summary>Failures omit the request URL because it carries the API key.</summary>
public sealed class AlphaVantageProvider(HttpClient client, IOptions<ExchangeProviderOptions> options) : IExchangeRateProvider
{
    public async Task<ProviderRate> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken)
    {
        var path = "query?function=CURRENCY_EXCHANGE_RATE" +
            $"&from_currency={Uri.EscapeDataString(baseCurrency)}" +
            $"&to_currency={Uri.EscapeDataString(quoteCurrency)}" +
            $"&apikey={Uri.EscapeDataString(options.Value.ApiKey)}";
        try
        {
            using var response = await client.GetAsync(path, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                throw new FxException(ErrorKind.ProviderUnavailable, "The exchange provider is temporarily unavailable.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw InvalidResponse();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw InvalidResponse();
            }
            // Quota and key failures arrive as HTTP 200 with "Note" or "Information" instead of data.
            if (root.TryGetProperty("Note", out _) || root.TryGetProperty("Information", out _))
            {
                throw new FxException(ErrorKind.ProviderUnavailable,
                    "The exchange provider could not serve this request. Check provider quota and configuration.");
            }
            if (!root.TryGetProperty("Realtime Currency Exchange Rate", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                throw InvalidResponse();
            }
            if (GetString(data, "1. From_Currency Code") != baseCurrency ||
                GetString(data, "3. To_Currency Code") != quoteCurrency)
            {
                throw InvalidResponse();
            }
            if (!PriceParser.TryParse(GetString(data, "8. Bid Price"), out var bid) ||
                !PriceParser.TryParse(GetString(data, "9. Ask Price"), out var ask))
            {
                throw InvalidResponse();
            }
            // The timestamp has no offset; require UTC to avoid storing the wrong instant.
            if (GetString(data, "7. Time Zone") != "UTC" ||
                !DateTimeOffset.TryParseExact(GetString(data, "6. Last Refreshed"), "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var quotedAt))
            {
                throw InvalidResponse();
            }
            if (RateRules.CheckPrices(bid, ask) is not null)
            {
                throw InvalidResponse();
            }
            return new ProviderRate(bid, ask, RateSources.AlphaVantage, quotedAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeouts use cancellation even when the caller has not cancelled.
            throw new FxException(ErrorKind.ProviderTimeout, "The exchange provider timed out.");
        }
        catch (HttpRequestException)
        {
            throw new FxException(ErrorKind.ProviderUnavailable, "The exchange provider could not be reached.");
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
        catch (IOException)
        {
            throw new FxException(ErrorKind.ProviderUnavailable, "The exchange provider response was interrupted.");
        }
    }

    private static string? GetString(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static FxException InvalidResponse() =>
        new(ErrorKind.ProviderFailure, "The exchange provider returned an invalid response.");
}
