using FxRates.Api.Contracts;
using FxRates.Api.RateLimiting;
using FxRates.Application.Rates;

namespace FxRates.Api.Endpoints;

public static class RateEndpoints
{
    public static void MapRateEndpoints(this WebApplication app, bool rateLimitingEnabled)
    {
        var group = app.MapGroup("/api/rates").WithTags("Rates");
        if (rateLimitingEnabled)
        {
            group.RequireRateLimiting(RateLimitingExtensions.PolicyName);
        }
        group.AddEndpointFilter(NoStoreFilter);
        group.ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/", async (RateService service, CancellationToken token) =>
                TypedResults.Ok((await service.ListAsync(token)).Select(RateResponse.From).ToList()))
            .WithName("ListRates")
            .WithSummary("List stored rates.")
            .WithDescription("Returns every stored rate sorted by base then quote currency. Never calls the provider.");

        group.MapGet("/{baseCurrency}/{quoteCurrency}", async (string baseCurrency, string quoteCurrency,
                RateService service, CancellationToken token) =>
                TypedResults.Ok(RateResponse.From(await service.GetAsync(baseCurrency, quoteCurrency, token))))
            .WithName("GetRate")
            .WithSummary("Get a stored rate, or fetch and store a missing pair.")
            .WithDescription("Returns the stored rate however old it is. When the pair is missing, fetches it from the " +
                "configured provider, stores it, and returns it. A failed provider call stores nothing.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapPost("/", async (CreateRateRequest request, RateService service, CancellationToken token) =>
            {
                var rate = await service.CreateAsync(request.ToInput(), token);
                return TypedResults.Created($"/api/rates/{rate.BaseCurrency}/{rate.QuoteCurrency}", RateResponse.From(rate));
            })
            .WithName("CreateRate")
            .WithSummary("Create a manual rate.")
            .WithDescription("Stores caller-supplied prices with source Manual. Returns 409 when the pair already exists.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{baseCurrency}/{quoteCurrency}", async (string baseCurrency, string quoteCurrency,
                UpdateRateRequest request, RateService service, CancellationToken token) =>
            {
                var rate = await service.UpdateAsync(request.ToInput(baseCurrency, quoteCurrency), token);
                return TypedResults.Ok(RateResponse.From(rate));
            })
            .WithName("UpdateRate")
            .WithSummary("Replace a stored rate with manual prices.")
            .WithDescription("Sets source to Manual and clears providerQuotedAt. Last write wins; there is no version check.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{baseCurrency}/{quoteCurrency}", async (string baseCurrency, string quoteCurrency,
                RateService service, CancellationToken token) =>
            {
                await service.DeleteAsync(baseCurrency, quoteCurrency, token);
                return TypedResults.NoContent();
            })
            .WithName("DeleteRate")
            .WithSummary("Delete a rate.")
            .WithDescription("Removes the stored rate. This is not a blocklist: the next lookup may fetch the pair again.")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Disable caching because GET can insert and mutations invalidate quotes. OnStarting keeps the
    /// header after the exception handler clears the response.
    /// </summary>
    private static async ValueTask<object?> NoStoreFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.OnStarting(static state =>
        {
            ((HttpResponse)state).Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        }, context.HttpContext.Response);
        return await next(context);
    }
}
