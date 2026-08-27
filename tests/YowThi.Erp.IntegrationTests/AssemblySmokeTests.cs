namespace YowThi.Erp.IntegrationTests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void Integration_test_project_bootstraps_without_a_database()
    {
        Assert.Equal(
            "YowThi.Erp.Infrastructure",
            typeof(Infrastructure.AssemblyReference).Assembly.GetName().Name);
    }
}
