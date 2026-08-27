namespace YowThi.Erp.Domain.Tests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void Domain_assembly_name_is_stable()
    {
        Assert.Equal("YowThi.Erp.Domain", typeof(Domain.AssemblyReference).Assembly.GetName().Name);
    }
}
