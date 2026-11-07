using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UTperfume.Application.Common;

namespace UTperfume.Infrastructure.Persistence;

/// <summary>
/// SQL Server implementation of <see cref="IProductWriteLock"/>: a read-committed transaction that holds an update lock
/// on the product row until commit or rollback (BR-A-25, database model section 3.6). A second request for the same
/// product waits at the lock, then counts the rows the first one committed.
/// </summary>
public sealed class ProductWriteLock(AppDbContext context) : IProductWriteLock
{
    public async Task<IAdminTransaction?> LockProductAsync(int productId, CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var found = await context.Database
                .SqlQuery<int>($"SELECT [Id] AS [Value] FROM [catalog].[Product] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {productId}")
                .ToListAsync(cancellationToken);
            if (found.Count == 0)
            {
                await transaction.DisposeAsync();
                return null;
            }

            return new Held(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class Held(IDbContextTransaction transaction) : IAdminTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
