using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Infrastructure.Persistence.Concurrency;

public sealed class RowVersionSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyRowVersions(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyRowVersions(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void ApplyRowVersions(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<IHasRowVersion>())
        {
            var rowVersionProperty = entry.Property(nameof(IHasRowVersion.RowVersion));

            switch (entry.State)
            {
                case EntityState.Added:
                    rowVersionProperty.CurrentValue = 1L;
                    break;

                case EntityState.Modified:
                    if (rowVersionProperty.OriginalValue is not long originalVersion || originalVersion < 1)
                    {
                        throw new InvalidOperationException(
                            "A modified row-version entity must have an original row version greater than or equal to 1.");
                    }

                    rowVersionProperty.CurrentValue = checked(originalVersion + 1);
                    break;
            }
        }
    }
}
