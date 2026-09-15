using System.Net;
using System.Text.Json;
using Xunit;

namespace FxRates.IntegrationTests.Api;

public sealed class RateLimitingApiTests : IAsyncLifetime
{
    private readonly ApiFixture api = new(rateLimitingEnabled: true);

    public Task InitializeAsync() => api.InitializeAsync();

    public Task DisposeAsync() => api.DisposeAsync();

    [Fact]
    public async Task Fourth_request_returns_problem_with_retry_after_and_trace_id()
    {
        for (var request = 0; request < 3; request++)
        {
            using var response = await api.Client.GetAsync("/api/rates");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejected = await api.Client.GetAsync("/api/rates");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(rejected.Headers.RetryAfter?.Delta);
        Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        using var document = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(429, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
    }
}
