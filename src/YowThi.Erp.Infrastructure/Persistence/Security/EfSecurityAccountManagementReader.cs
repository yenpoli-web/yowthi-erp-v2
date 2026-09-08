using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Security;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Security;

internal sealed class EfSecurityAccountManagementReader : ISecurityAccountManagementReader
{
    private readonly ErpDbContext _dbContext;

    public EfSecurityAccountManagementReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<SecurityAccountManagementPage> GetAsync(
        string? search,
        CancellationToken cancellationToken)
    {
        IQueryable<SystemAccountRecord> accounts = _dbContext.Set<SystemAccountRecord>().AsNoTracking();
        var normalizedSearch = search?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var pattern = $"%{normalizedSearch}%";
            accounts = accounts.Where(x =>
                EF.Functions.ILike(x.DisplayName, pattern)
                || (x.IdentityIssuer != null && EF.Functions.ILike(x.IdentityIssuer, pattern))
                || (x.IdentitySubject != null && EF.Functions.ILike(x.IdentitySubject, pattern)));
        }

        var rows = await accounts
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.DisplayName,
                x.Active,
                x.IdentityIssuer,
                x.IdentitySubject,
                x.RowVersion,
                x.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var accountIds = rows.Select(x => x.Id).ToArray();
        var grants = accountIds.Length == 0
            ? []
            : await _dbContext.Set<SystemAccountCapabilityGrantRecord>()
                .AsNoTracking()
                .Where(x => accountIds.Contains(x.AccountId) && x.Active)
                .OrderBy(x => x.CapabilityName)
                .Select(x => new { x.AccountId, x.CapabilityName })
                .ToListAsync(cancellationToken);

        var capabilitiesByAccount = grants
            .GroupBy(x => x.AccountId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(x => x.CapabilityName).ToArray());

        var items = rows.Select(row => new SecurityAccountItem(
            row.Id,
            row.DisplayName,
            row.Active,
            row.IdentityIssuer,
            row.IdentitySubject,
            row.RowVersion,
            row.CreatedAt,
            capabilitiesByAccount.GetValueOrDefault(row.Id) ?? [],
            string.Equals(row.IdentityIssuer, DevelopmentTestAdmin.IdentityIssuer, StringComparison.Ordinal)
                && string.Equals(row.IdentitySubject, DevelopmentTestAdmin.IdentitySubject, StringComparison.Ordinal)))
            .ToArray();

        return new SecurityAccountManagementPage(items, SecurityCapabilities.All);
    }
}
