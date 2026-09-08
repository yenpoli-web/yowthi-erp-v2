using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Security;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Security;

internal sealed class EfSecurityActorResolver : ISecurityActorResolver
{
    private readonly ErpDbContext _dbContext;

    public EfSecurityActorResolver(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<SecurityResolvedActor?> ResolveAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty)
        {
            return null;
        }

        var account = await _dbContext.Set<SystemAccountRecord>()
            .AsNoTracking()
            .Where(x => x.Id == accountId && x.Active)
            .Select(x => new { x.Id, x.DisplayName })
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        var capabilities = await _dbContext.Set<SystemAccountCapabilityGrantRecord>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Active)
            .OrderBy(x => x.CapabilityName)
            .Select(x => x.CapabilityName)
            .ToArrayAsync(cancellationToken);

        return new SecurityResolvedActor(account.Id, account.DisplayName, capabilities);
    }
}
