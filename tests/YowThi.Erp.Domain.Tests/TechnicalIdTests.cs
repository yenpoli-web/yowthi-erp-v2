using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Tests;

public sealed class TechnicalIdTests
{
    [Fact]
    public void NewVersion7_returns_non_empty_version_7_guid()
    {
        var id = TechnicalId.NewVersion7();

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(7, id.Version);
    }
}
