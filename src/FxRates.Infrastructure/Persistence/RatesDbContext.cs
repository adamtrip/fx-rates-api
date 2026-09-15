using FxRates.Application.Rates;
using Microsoft.EntityFrameworkCore;

namespace FxRates.Infrastructure.Persistence;

public sealed class RatesDbContext(DbContextOptions<RatesDbContext> options) : DbContext(options)
{
    public DbSet<RateEntity> Rates => Set<RateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var rate = modelBuilder.Entity<RateEntity>();
        rate.ToTable("Rates", table =>
        {
            // Enforce domain rules even if a service bug bypasses validation.
            table.HasCheckConstraint("CK_Rates_Prices", "\"Bid\" > 0 AND \"Ask\" >= \"Bid\"");
            table.HasCheckConstraint("CK_Rates_DifferentCurrencies", "\"BaseCurrency\" <> \"QuoteCurrency\"");
        });
        // The primary key arbitrates concurrent inserts; TryAddAsync maps a unique violation to false.
        rate.HasKey(x => new { x.BaseCurrency, x.QuoteCurrency });
        rate.Property(x => x.BaseCurrency).HasMaxLength(3);
        rate.Property(x => x.QuoteCurrency).HasMaxLength(3);
        rate.Property(x => x.Bid).HasPrecision(PricePrecision.TotalDigits, PricePrecision.FractionDigits);
        rate.Property(x => x.Ask).HasPrecision(PricePrecision.TotalDigits, PricePrecision.FractionDigits);
        rate.Property(x => x.Source).HasMaxLength(32);
    }
}
