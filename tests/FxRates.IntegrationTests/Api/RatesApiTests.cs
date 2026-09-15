using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FxRates.Api.Contracts;
using FxRates.Application.Errors;
using FxRates.Application.Rates;
using Xunit;

namespace FxRates.IntegrationTests.Api;

// One fixture and one test class keep database resets sequential while individual tests can exercise concurrency.
public sealed class RatesApiTests(ApiFixture api) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => api.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Manual_crud_normalizes_codes_and_preserves_decimal_prices()
    {
        var request = new CreateRateRequest("usd", "eur", 0.91234567m, 0.92345678m);
        var response = await api.Client.PostAsJsonAsync("/api/rates", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith("/api/rates/USD/EUR", response.Headers.Location?.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var created = await ReadRate(response);
        Assert.Equal("USD", created.BaseCurrency);
        Assert.Equal("EUR", created.QuoteCurrency);
        Assert.Equal(RateSources.Manual, created.Source);
        Assert.Null(created.ProviderQuotedAt);
        Assert.NotEqual(default, created.UpdatedAt);
        Assert.Equal(0.91234567m, created.Bid);

        var read = await api.Client.GetFromJsonAsync<RateResponse>("/api/rates/usd/eur");
        Assert.Equal(created, read);
        var updatedResponse = await api.Client.PutAsJsonAsync("/api/rates/USD/EUR", new UpdateRateRequest(0.8m, 0.9m));
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        Assert.Equal(0.8m, (await ReadRate(updatedResponse)).Bid);
        Assert.Single(await ListRates());
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync("/api/rates/USD/EUR")).StatusCode);
        Assert.Empty(await ListRates());
        Assert.Equal(0, api.Provider.Calls);
    }

    [Fact]
    public async Task Get_after_create_omits_database_fractional_padding()
    {
        using var created = await api.Client.PostAsJsonAsync("/api/rates", new CreateRateRequest("USD", "EUR", 0.91m, 0.92m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var json = await api.Client.GetStringAsync("/api/rates/USD/EUR");

        Assert.Contains("\"bid\":0.91", json);
        Assert.DoesNotContain("\"bid\":0.91000000", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("0.91", document.RootElement.GetProperty("bid").GetRawText());
        Assert.Equal("0.92", document.RootElement.GetProperty("ask").GetRawText());
    }

    [Fact]
    public async Task Duplicate_create_conflicts_and_missing_mutations_return_not_found()
    {
        var input = new CreateRateRequest("USD", "EUR", 1m, 2m);
        Assert.Equal(HttpStatusCode.Created, (await api.Client.PostAsJsonAsync("/api/rates", input)).StatusCode);

        await AssertProblem(await api.Client.PostAsJsonAsync("/api/rates", input with { BaseCurrency = "usd" }), 409);
        await AssertProblem(await api.Client.PutAsJsonAsync("/api/rates/GBP/JPY", new UpdateRateRequest(1, 2)), 404);
        await AssertProblem(await api.Client.DeleteAsync("/api/rates/GBP/JPY"), 404);
        Assert.Equal(0, api.Provider.Calls);
    }

    [Theory]
    [InlineData("XXX", "EUR", "1", "2")]
    [InlineData("USD", "USD", "1", "2")]
    [InlineData("USD", "EUR", "0", "2")]
    [InlineData("USD", "EUR", "-1", "2")]
    [InlineData("USD", "EUR", "2", "1")]
    [InlineData("USD", "EUR", "1.000000001", "2")]
    [InlineData("USD", "EUR", "1", "10000000000")]
    public async Task Invalid_input_is_rejected_without_writing(string baseCurrency, string quoteCurrency, string bid, string ask)
    {
        var input = new CreateRateRequest(baseCurrency, quoteCurrency, Price(bid), Price(ask));

        await AssertProblem(await api.Client.PostAsJsonAsync("/api/rates", input), 400);

        Assert.Empty(await ListRates());
        Assert.Equal(0, api.Provider.Calls);
    }

    [Fact]
    public async Task Missing_body_fields_are_rejected_as_validation_errors()
    {
        using var body = new StringContent("{\"bid\":1,\"ask\":2}", Encoding.UTF8, "application/json");

        await AssertProblem(await api.Client.PostAsync("/api/rates", body), 400);

        Assert.Empty(await ListRates());
    }

    [Fact]
    public async Task Invalid_lookup_never_calls_provider()
    {
        await AssertProblem(await api.Client.GetAsync("/api/rates/USD/XXX"), 400);
        await AssertProblem(await api.Client.GetAsync("/api/rates/USD/USD"), 400);
        Assert.Equal(0, api.Provider.Calls);
    }

    [Theory]
    [InlineData("1.00000000000000000000000000001")]
    [InlineData("1e0")]
    [InlineData("1E+0")]
    public async Task Raw_price_precision_and_exponent_notation_are_rejected_before_create(string rawPrice)
    {
        using var body = new StringContent(
            "{\"baseCurrency\":\"USD\",\"quoteCurrency\":\"EUR\",\"bid\":" + rawPrice + ",\"ask\":2}",
            Encoding.UTF8, "application/json");

        var problem = await AssertProblem(await api.Client.PostAsync("/api/rates", body), 400);
        Assert.Contains("decimal notation", problem.GetProperty("detail").GetString());

        Assert.Empty(await ListRates());
        Assert.Equal(0, api.Provider.Calls);
    }

    [Theory]
    [InlineData("1.00000000000000000000000000001")]
    [InlineData("1e0")]
    [InlineData("1E+0")]
    public async Task Raw_price_precision_and_exponent_notation_are_rejected_without_changing_existing_rate(string rawPrice)
    {
        var request = new CreateRateRequest("USD", "EUR", 0.8m, 0.9m);
        var existing = await ReadRate(await api.Client.PostAsJsonAsync("/api/rates", request));
        using var body = new StringContent("{\"bid\":0.5,\"ask\":" + rawPrice + "}", Encoding.UTF8, "application/json");

        var problem = await AssertProblem(await api.Client.PutAsync("/api/rates/USD/EUR", body), 400);
        Assert.Contains("decimal notation", problem.GetProperty("detail").GetString());

        Assert.Equal(existing, await api.Client.GetFromJsonAsync<RateResponse>("/api/rates/USD/EUR"));
        Assert.Single(await ListRates());
        Assert.Equal(0, api.Provider.Calls);
    }

    [Fact]
    public async Task Opposite_pairs_are_independent()
    {
        await api.Client.PostAsJsonAsync("/api/rates", new CreateRateRequest("USD", "EUR", 1m, 2m));
        await api.Client.PostAsJsonAsync("/api/rates", new CreateRateRequest("EUR", "USD", 3m, 4m));

        Assert.Equal(2, (await ListRates()).Length);
        Assert.Equal(3m, (await api.Client.GetFromJsonAsync<RateResponse>("/api/rates/EUR/USD"))!.Bid);
        Assert.Equal(0, api.Provider.Calls);
    }

    [Fact]
    public async Task Stored_old_rate_is_returned_without_provider_access()
    {
        var storedAt = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var old = new Rate("USD", "EUR", 1m, 2m, RateSources.AlphaVantage, storedAt, null);
        await api.SeedAsync(old);
        api.Provider.Handler = _ => throw new InvalidOperationException("Provider must not be called");

        Assert.Equal(RateResponse.From(old), await api.Client.GetFromJsonAsync<RateResponse>("/api/rates/USD/EUR"));
        Assert.Equal(0, api.Provider.Calls);
    }

    [Fact]
    public async Task Missing_rate_is_fetched_once_persisted_and_recreated_after_deletion()
    {
        var first = await api.Client.GetFromJsonAsync<RateResponse>("/api/rates/USD/EUR");
        Assert.Equal(RateSources.Fake, first!.Source);
        Assert.NotNull(first.ProviderQuotedAt);
        Assert.Equal(first, await api.Client.GetFromJsonAsync<RateResponse>("/api/rates/USD/EUR"));
        Assert.Equal(1, api.Provider.Calls);
        Assert.Single(await ListRates());

        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync("/api/rates/USD/EUR")).StatusCode);
        (await api.Client.GetAsync("/api/rates/USD/EUR")).EnsureSuccessStatusCode();
        Assert.Equal(2, api.Provider.Calls);
    }

    [Fact]
    public async Task Manual_update_clears_provider_timestamp_and_survives_subsequent_reads()
    {
        await api.Client.GetAsync("/api/rates/USD/EUR");

        var updated = await ReadRate(await api.Client.PutAsJsonAsync("/api/rates/USD/EUR", new UpdateRateRequest(3m, 4m)));

        Assert.Equal(RateSources.Manual, updated.Source);
        Assert.Null(updated.ProviderQuotedAt);
        Assert.Equal(updated, await api.Client.GetFromJsonAsync<RateResponse>("/api/rates/USD/EUR"));
        Assert.Equal(1, api.Provider.Calls);
    }

    [Theory]
    [InlineData(ErrorKind.ProviderFailure, 502)]
    [InlineData(ErrorKind.ProviderUnavailable, 503)]
    [InlineData(ErrorKind.ProviderTimeout, 504)]
    public async Task Provider_failure_returns_problem_and_saves_nothing(ErrorKind kind, int expectedStatus)
    {
        api.Provider.Handler = _ => throw new FxException(kind, "Provider request failed.");

        await AssertProblem(await api.Client.GetAsync("/api/rates/USD/EUR"), expectedStatus);

        Assert.Empty(await ListRates());
    }

    [Fact]
    public async Task Invalid_provider_prices_are_not_persisted()
    {
        api.Provider.Handler = _ => Task.FromResult(new ProviderRate(3m, 2m, RateSources.Fake, null));

        await AssertProblem(await api.Client.GetAsync("/api/rates/USD/EUR"), 502);

        Assert.Empty(await ListRates());
    }

    [Fact]
    public async Task Unexpected_exception_returns_generic_problem_with_trace_id()
    {
        api.Provider.Handler = _ => throw new InvalidOperationException("connection string with secret");

        var response = await api.Client.GetAsync("/api/rates/USD/EUR");

        var problem = await AssertProblem(response, 500);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Concurrent_missing_lookups_return_one_persisted_record()
    {
        const int count = 8;
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        api.Provider.Handler = async cancellationToken =>
        {
            // Hold every request inside the provider until all of them are there, so they all race the insert.
            if (Interlocked.Increment(ref entered) == count)
            {
                allEntered.SetResult();
            }
            await allEntered.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return new ProviderRate(1m, 2m, RateSources.Fake, null);
        };

        var responses = await Task.WhenAll(Enumerable.Range(0, count).Select(_ => api.Client.GetAsync("/api/rates/USD/EUR")));

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var rates = await Task.WhenAll(responses.Select(ReadRate));
        Assert.All(rates, rate => Assert.Equal(rates[0], rate));
        Assert.Single(await ListRates());
    }

    [Fact]
    public async Task Manual_creation_during_provider_fetch_is_not_overwritten()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Provider.Handler = async cancellationToken =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return new ProviderRate(1m, 2m, RateSources.Fake, null);
        };

        var lookup = api.Client.GetAsync("/api/rates/USD/EUR");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            var created = await api.Client.PostAsJsonAsync("/api/rates", new CreateRateRequest("USD", "EUR", 3m, 4m));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        finally
        {
            release.SetResult();
        }

        var returned = await ReadRate(await lookup);
        Assert.Equal(RateSources.Manual, returned.Source);
        Assert.Equal(3m, returned.Bid);
        Assert.Single(await ListRates());
    }

    [Fact]
    public async Task Malformed_json_and_unknown_routes_have_problem_responses()
    {
        using var body = new StringContent("{", Encoding.UTF8, "application/json");
        var problem = await AssertProblem(await api.Client.PostAsync("/api/rates", body), 400);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
        await AssertProblem(await api.Client.GetAsync("/api/does-not-exist"), 404);
    }

    [Fact]
    public async Task Health_endpoints_answer()
    {
        Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Openapi_document_exposes_rate_operations_with_descriptions()
    {
        var response = await api.Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var paths = document.RootElement.GetProperty("paths");
        Assert.Contains(paths.EnumerateObject(), path =>
            path.Name.TrimEnd('/') == "/api/rates" && path.Value.TryGetProperty("post", out _));
        Assert.Contains(paths.EnumerateObject(), path => path.Value.TryGetProperty("put", out _));
        var request = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateRateRequest");
        var bidDescription = request.GetProperty("properties").GetProperty("bid").GetProperty("description").GetString();
        Assert.False(string.IsNullOrWhiteSpace(bidDescription));
    }

    [Fact]
    public async Task Openapi_server_url_uses_the_forwarded_scheme()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await api.Client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var server = document.RootElement.GetProperty("servers")[0].GetProperty("url").GetString();
        Assert.StartsWith("https://", server);
    }

    private async Task<RateResponse[]> ListRates() => (await api.Client.GetFromJsonAsync<RateResponse[]>("/api/rates"))!;

    private static decimal Price(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<RateResponse> ReadRate(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RateResponse>())!;
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(status, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("title").GetString()));
        return document.RootElement.Clone();
    }
}
