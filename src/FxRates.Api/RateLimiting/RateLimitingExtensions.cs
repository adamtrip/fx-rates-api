using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FxRates.Api.RateLimiting;

public static class RateLimitingExtensions
{
    public const string PolicyName = "api";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitSettings>()
            .BindConfiguration(RateLimitSettings.SectionName)
            .Validate(settings => settings.PermitLimit >= 1, "RateLimiting:PermitLimit must be at least 1.")
            .Validate(settings => settings.WindowSeconds >= 1, "RateLimiting:WindowSeconds must be at least 1.")
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitSettings>>((options, settings) =>
        {
            options.AddFixedWindowLimiter(PolicyName, limiter =>
            {
                limiter.PermitLimit = settings.Value.PermitLimit;
                limiter.Window = TimeSpan.FromSeconds(settings.Value.WindowSeconds);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }
                await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Request limit exceeded",
                    detail: "The demo request limit was reached. Try again later.").ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }
}
