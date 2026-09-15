using FxRates.Application.Providers;
using FxRates.Application.Rates;
using FxRates.Application.Storage;
using FxRates.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace FxRates.IntegrationTests.Api;

/// <summary>
/// Hosts the real API against a disposable PostgreSQL container, with the exchange provider replaced
/// by <see cref="RecordingProvider"/> so tests control every quote without network access.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly bool rateLimitingEnabled;
    private TestApiFactory factory = null!;

    public ApiFixture() : this(false) { }

    internal ApiFixture(bool rateLimitingEnabled)
    {
        this.rateLimitingEnabled = rateLimitingEnabled;
    }

    public HttpClient Client { get; private set; } = null!;

    public RecordingProvider Provider { get; } = new();

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        factory = new TestApiFactory(database.GetConnectionString(), Provider, rateLimitingEnabled);
        Client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RatesDbContext>().Database.MigrateAsync();
    }

    /// <summary>Empties the table and forgets provider calls. Runs before every test.</summary>
    public async Task ResetAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RatesDbContext>().Rates.ExecuteDeleteAsync();
        Provider.Reset();
    }

    /// <summary>Inserts a row directly, bypassing the API, for tests that need a pre-existing rate.</summary>
    public async Task SeedAsync(Rate rate)
    {
        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IRateStore>().TryAddAsync(rate, CancellationToken.None));
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }
        await database.DisposeAsync();
    }

    private sealed class TestApiFactory(string connectionString, RecordingProvider provider, bool rateLimitingEnabled)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            // UseSetting runs before Program registers services, which read these values eagerly.
            builder.UseSetting("ConnectionStrings:Rates", connectionString);
            builder.UseSetting("ExchangeProvider:Mode", "Fake");
            builder.UseSetting("Messaging:Mode", "Logging");
            builder.UseSetting("RateLimiting:Enabled", rateLimitingEnabled.ToString());
            builder.UseSetting("RateLimiting:PermitLimit", "3");
            builder.UseSetting("RateLimiting:WindowSeconds", "60");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExchangeRateProvider>();
                services.AddSingleton<IExchangeRateProvider>(provider);
            });
        }
    }
}

/// <summary>Counts calls and answers with whatever <see cref="Handler"/> a test assigns.</summary>
public sealed class RecordingProvider : IExchangeRateProvider
{
    private int calls;

    public int Calls => Volatile.Read(ref calls);

    public Func<CancellationToken, Task<ProviderRate>> Handler { get; set; } = DefaultResponse;

    public Task<ProviderRate> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        return Handler(cancellationToken);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref calls, 0);
        Handler = DefaultResponse;
    }

    private static Task<ProviderRate> DefaultResponse(CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderRate(0.91m, 0.92m, RateSources.Fake, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
}
