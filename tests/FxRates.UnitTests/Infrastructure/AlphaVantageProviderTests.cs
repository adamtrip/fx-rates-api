using System.Net;
using FxRates.Application.Errors;
using FxRates.Application.Rates;
using FxRates.Infrastructure.Providers;
using Microsoft.Extensions.Options;
using Xunit;

namespace FxRates.UnitTests.Infrastructure;

public sealed class AlphaVantageProviderTests
{
    [Theory]
    [InlineData("test-key", "test-key")]
    [InlineData("a&b=c", "a%26b%3Dc")]
    public async Task Sends_get_request_with_pair_and_escaped_configured_api_key(string apiKey, string encodedApiKey)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Payload()) }));
        using var client = Client(handler);
        var provider = new AlphaVantageProvider(client, Options.Create(new ExchangeProviderOptions { ApiKey = apiKey }));

        await provider.GetAsync("USD", "EUR", default);

        var request = Assert.IsType<HttpRequestMessage>(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, request.Method);
        var uri = Assert.IsType<Uri>(request.RequestUri);
        Assert.Equal("www.alphavantage.co", uri.Host);
        Assert.Equal("/query", uri.AbsolutePath);
        var query = uri.Query.TrimStart('?').Split('&')
            .Select(parameter => parameter.Split('=', 2))
            .ToDictionary(parameter => Uri.UnescapeDataString(parameter[0]), parameter => Uri.UnescapeDataString(parameter[1]));
        Assert.Equal(4, query.Count);
        Assert.Equal("CURRENCY_EXCHANGE_RATE", query["function"]);
        Assert.Equal("USD", query["from_currency"]);
        Assert.Equal("EUR", query["to_currency"]);
        Assert.Equal(apiKey, query["apikey"]);
        Assert.Contains($"apikey={encodedApiKey}", uri.Query);
    }

    [Fact]
    public async Task Parses_bid_ask_and_utc_timestamp()
    {
        var rate = await Provider(Payload()).GetAsync("USD", "EUR", default);

        Assert.Equal(0.9m, rate.Bid);
        Assert.Equal(0.91m, rate.Ask);
        Assert.Equal(RateSources.AlphaVantage, rate.Source);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero), rate.QuotedAt);
    }

    [Theory]
    [InlineData("{}", ErrorKind.ProviderFailure)]
    [InlineData("[]", ErrorKind.ProviderFailure)]
    [InlineData("broken", ErrorKind.ProviderFailure)]
    [InlineData("{\"Error Message\":\"Invalid API call\"}", ErrorKind.ProviderFailure)]
    [InlineData("{\"Note\":\"limit reached\"}", ErrorKind.ProviderUnavailable)]
    [InlineData("{\"Information\":\"limit reached\"}", ErrorKind.ProviderUnavailable)]
    public Task Rejects_error_and_malformed_payloads(string json, ErrorKind kind) => AssertFails(json, kind);

    [Fact]
    public Task Rejects_payload_for_a_different_pair() => AssertInvalidResponse(Payload(to: "GBP"));

    [Fact]
    public Task Rejects_bid_above_ask() => AssertInvalidResponse(Payload(bid: "0.92000000", ask: "0.90000000"));

    [Fact]
    public Task Rejects_ask_with_nine_fractional_digits() => AssertInvalidResponse(Payload(ask: "0.900000001"));

    [Fact]
    public Task Rejects_ask_with_excess_digits_that_would_round_to_valid() =>
        AssertInvalidResponse(Payload(ask: "0.90000000000000000000000000001"));

    [Fact]
    public Task Rejects_zero_price() => AssertInvalidResponse(Payload(ask: "0"));

    [Fact]
    public Task Rejects_non_utc_time_zone() => AssertInvalidResponse(Payload(timeZone: "EST"));

    [Fact]
    public Task Rejects_unparseable_timestamp() => AssertInvalidResponse(Payload(lastRefreshed: "yesterday"));

    [Fact]
    public Task Rejects_missing_ask_price() => AssertInvalidResponse(Payload(includeAsk: false));

    [Theory]
    [InlineData(429, ErrorKind.ProviderUnavailable)]
    [InlineData(503, ErrorKind.ProviderUnavailable)]
    [InlineData(401, ErrorKind.ProviderFailure)]
    public Task Maps_http_failures(int status, ErrorKind kind) => AssertFails("{}", kind, status);

    [Fact]
    public async Task Timeout_has_safe_typed_error()
    {
        using var client = Client(new StubHandler((_, _) => throw new TaskCanceledException("secret url")));
        var provider = new AlphaVantageProvider(client, Options.Create(new ExchangeProviderOptions()));

        var exception = await Assert.ThrowsAsync<FxException>(() => provider.GetAsync("USD", "EUR", default));

        Assert.Equal(ErrorKind.ProviderTimeout, exception.Kind);
        Assert.DoesNotContain("secret", exception.ToString());
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = Client(new StubHandler((_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }));
        var provider = new AlphaVantageProvider(client, Options.Create(new ExchangeProviderOptions()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetAsync("USD", "EUR", cancellation.Token));
    }

    [Fact]
    public async Task Network_error_does_not_expose_request_url()
    {
        using var client = Client(new StubHandler((_, _) => throw new HttpRequestException("url?apikey=secret")));
        var provider = new AlphaVantageProvider(client, Options.Create(new ExchangeProviderOptions()));

        var exception = await Assert.ThrowsAsync<FxException>(() => provider.GetAsync("USD", "EUR", default));

        Assert.Equal(ErrorKind.ProviderUnavailable, exception.Kind);
        Assert.DoesNotContain("secret", exception.ToString());
    }

    /// <summary>A valid Alpha Vantage response for USD/EUR; each parameter overrides one field.</summary>
    private static string Payload(
        string from = "USD",
        string to = "EUR",
        string bid = "0.90000000",
        string ask = "0.91000000",
        string lastRefreshed = "2026-09-11 12:00:00",
        string timeZone = "UTC",
        bool includeAsk = true)
    {
        var askField = includeAsk ? $", \"9. Ask Price\": \"{ask}\"" : "";
        return $$$"""
            {"Realtime Currency Exchange Rate": {
              "1. From_Currency Code": "{{{from}}}",
              "3. To_Currency Code": "{{{to}}}",
              "6. Last Refreshed": "{{{lastRefreshed}}}",
              "7. Time Zone": "{{{timeZone}}}",
              "8. Bid Price": "{{{bid}}}"{{{askField}}}
            }}
            """;
    }

    private static Task AssertInvalidResponse(string json) => AssertFails(json, ErrorKind.ProviderFailure);

    private static async Task AssertFails(string json, ErrorKind kind, int status = 200)
    {
        var exception = await Assert.ThrowsAsync<FxException>(() => Provider(json, status).GetAsync("USD", "EUR", default));
        Assert.Equal(kind, exception.Kind);
    }

    private static AlphaVantageProvider Provider(string body, int status = 200)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) }));
        return new AlphaVantageProvider(Client(handler), Options.Create(new ExchangeProviderOptions { ApiKey = "test-key" }));
    }

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://www.alphavantage.co/") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return send(request, cancellationToken);
        }
    }
}
