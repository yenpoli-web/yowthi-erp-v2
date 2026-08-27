namespace YowThi.Erp.Api.ContractTests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void Api_assembly_name_is_stable()
    {
        Assert.Equal("YowThi.Erp.Api", typeof(Api.AssemblyReference).Assembly.GetName().Name);
    }
}
