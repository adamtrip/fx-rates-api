using FxRates.Application.Rates;
using FxRates.Application.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FxRates.Infrastructure.Persistence;

public sealed class PostgresRateStore(RatesDbContext db) : IRateStore
{
    public async Task<IReadOnlyList<Rate>> ListAsync(CancellationToken cancellationToken) =>
        await db.Rates.AsNoTracking()
            .OrderBy(x => x.BaseCurrency).ThenBy(x => x.QuoteCurrency)
            .Select(x => ToRate(x))
            .ToListAsync(cancellationToken);

    public Task<Rate?> FindAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken) =>
        db.Rates.AsNoTracking()
            .Where(x => x.BaseCurrency == baseCurrency && x.QuoteCurrency == quoteCurrency)
            .Select(x => ToRate(x))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TryAddAsync(Rate rate, CancellationToken cancellationToken)
    {
        var entity = new RateEntity
        {
            BaseCurrency = rate.BaseCurrency,
            QuoteCurrency = rate.QuoteCurrency,
            Bid = rate.Bid,
            Ask = rate.Ask,
            Source = rate.Source,
            UpdatedAt = rate.UpdatedAt,
            ProviderQuotedAt = rate.ProviderQuotedAt
        };
        db.Rates.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
        finally
        {
            // Failed SaveChanges leaves the row Added; detach it to prevent a later save from retrying it.
            db.Entry(entity).State = EntityState.Detached;
        }
    }

    public async Task<bool> UpdateAsync(Rate rate, CancellationToken cancellationToken) =>
        await db.Rates
            .Where(x => x.BaseCurrency == rate.BaseCurrency && x.QuoteCurrency == rate.QuoteCurrency)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Bid, rate.Bid)
                .SetProperty(x => x.Ask, rate.Ask)
                .SetProperty(x => x.Source, rate.Source)
                .SetProperty(x => x.UpdatedAt, rate.UpdatedAt)
                .SetProperty(x => x.ProviderQuotedAt, rate.ProviderQuotedAt), cancellationToken) == 1;

    public async Task<bool> DeleteAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken) =>
        await db.Rates
            .Where(x => x.BaseCurrency == baseCurrency && x.QuoteCurrency == quoteCurrency)
            .ExecuteDeleteAsync(cancellationToken) == 1;

    private static Rate ToRate(RateEntity x) =>
        new(x.BaseCurrency, x.QuoteCurrency, x.Bid, x.Ask, x.Source, x.UpdatedAt, x.ProviderQuotedAt);
}
