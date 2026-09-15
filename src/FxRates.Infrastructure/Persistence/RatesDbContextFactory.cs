using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FxRates.Infrastructure.Persistence;

/// <summary>Keeps EF tooling and migration bundles independent of provider and messaging configuration.</summary>
public sealed class RatesDbContextFactory : IDesignTimeDbContextFactory<RatesDbContext>
{
    public RatesDbContext CreateDbContext(string[] args)
    {
        // ConnectionStrings__Rates overrides the local development connection.
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Rates")
            ?? "Host=localhost;Port=5432;Database=fxrates;Username=fxrates;Password=fxrates-dev";
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("ConnectionStrings__Rates cannot be blank.");
        }
        return new RatesDbContext(new DbContextOptionsBuilder<RatesDbContext>().UseNpgsql(connection).Options);
    }
}
