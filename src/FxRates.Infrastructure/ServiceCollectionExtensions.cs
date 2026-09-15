using FxRates.Application.Events;
using FxRates.Application.Providers;
using FxRates.Application.Rates;
using FxRates.Application.Storage;
using FxRates.Infrastructure.Messaging;
using FxRates.Infrastructure.Persistence;
using FxRates.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FxRates.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateStorage(configuration);
        services.AddExchangeProvider(configuration);
        services.AddEventPublishing(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<RateService>();
        return services;
    }

    private static void AddRateStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Rates");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("ConnectionStrings:Rates must be configured.");
        }
        services.AddDbContext<RatesDbContext>(options => options.UseNpgsql(connection));
        services.AddScoped<IRateStore, PostgresRateStore>();
    }

    private static void AddExchangeProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExchangeProviderOptions>()
            .Bind(configuration.GetSection(ExchangeProviderOptions.SectionName))
            .Validate(options => Enum.IsDefined(options.Mode), "ExchangeProvider:Mode must be AlphaVantage or Fake.")
            .Validate(options => options.Mode != ExchangeProviderMode.AlphaVantage || !string.IsNullOrWhiteSpace(options.ApiKey),
                "ExchangeProvider:ApiKey is required in AlphaVantage mode.")
            .Validate(options => options.TimeoutSeconds is >= 1 and <= 60,
                "ExchangeProvider:TimeoutSeconds must be between 1 and 60.")
            .ValidateOnStart();

        services.AddSingleton<FakeExchangeRateProvider>();
        services.AddHttpClient<AlphaVantageProvider>((provider, client) =>
        {
            client.BaseAddress = new Uri("https://www.alphavantage.co/");
            var settings = provider.GetRequiredService<IOptions<ExchangeProviderOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            // Bound response memory if the upstream sends an oversized body.
            client.MaxResponseContentBufferSize = 64 * 1024;
        })
        // Default HTTP logging exposes the API key in the request URI.
        .RemoveAllLoggers();

        // Chosen per resolution because the typed HttpClient above is transient by design.
        services.AddScoped<IExchangeRateProvider>(provider =>
            provider.GetRequiredService<IOptions<ExchangeProviderOptions>>().Value.Mode == ExchangeProviderMode.Fake
                ? provider.GetRequiredService<FakeExchangeRateProvider>()
                : provider.GetRequiredService<AlphaVantageProvider>());
    }

    private static void AddEventPublishing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MessagingOptions>()
            .Bind(configuration.GetSection(MessagingOptions.SectionName))
            .Validate(options => Enum.IsDefined(options.Mode), "Messaging:Mode must be Logging or RabbitMq.")
            .Validate(options => options.Mode != EventPublisherMode.RabbitMq || !string.IsNullOrWhiteSpace(options.Host),
                "Messaging:Host is required in RabbitMq mode.")
            .Validate(options => options.Mode != EventPublisherMode.RabbitMq || !string.IsNullOrWhiteSpace(options.UserName),
                "Messaging:UserName is required in RabbitMq mode.")
            .Validate(options => options.Port is >= 1 and <= 65535, "Messaging:Port must be a valid TCP port.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Exchange), "Messaging:Exchange must not be blank.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Queue), "Messaging:Queue must not be blank.")
            .ValidateOnStart();

        // Resolve only the selected publisher so Logging mode never opens a broker connection.
        services.AddSingleton<LoggingEventPublisher>();
        services.AddSingleton<RabbitMqEventPublisher>();
        services.AddSingleton<IEventPublisher>(provider =>
            provider.GetRequiredService<IOptions<MessagingOptions>>().Value.Mode == EventPublisherMode.RabbitMq
                ? provider.GetRequiredService<RabbitMqEventPublisher>()
                : provider.GetRequiredService<LoggingEventPublisher>());
    }
}
