using System.Xml.Linq;

namespace YowThi.Erp.ArchitectureTests;

public sealed class ProjectDependencyTests
{
    [Fact]
    public void Production_project_references_follow_the_approved_direction()
    {
        AssertProjectReferences(
            "src/YowThi.Erp.Domain/YowThi.Erp.Domain.csproj");

        AssertProjectReferences(
            "src/YowThi.Erp.Application/YowThi.Erp.Application.csproj",
            "src/YowThi.Erp.Domain/YowThi.Erp.Domain.csproj");

        AssertProjectReferences(
            "src/YowThi.Erp.Infrastructure/YowThi.Erp.Infrastructure.csproj",
            "src/YowThi.Erp.Application/YowThi.Erp.Application.csproj",
            "src/YowThi.Erp.Domain/YowThi.Erp.Domain.csproj");

        AssertProjectReferences(
            "src/YowThi.Erp.Infrastructure.Migrations/YowThi.Erp.Infrastructure.Migrations.csproj",
            "src/YowThi.Erp.Infrastructure/YowThi.Erp.Infrastructure.csproj");

        AssertProjectReferences(
            "src/YowThi.Erp.Api/YowThi.Erp.Api.csproj",
            "src/YowThi.Erp.Application/YowThi.Erp.Application.csproj",
            "src/YowThi.Erp.Infrastructure/YowThi.Erp.Infrastructure.csproj");
    }

    private static void AssertProjectReferences(string projectPath, params string[] expectedReferences)
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectFullPath = Path.Combine(repositoryRoot, projectPath);
        var projectDirectory = Path.GetDirectoryName(projectFullPath)
            ?? throw new InvalidOperationException($"Cannot resolve project directory for {projectPath}.");

        var document = XDocument.Load(projectFullPath);
        var actualReferences = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(Path.Combine(projectDirectory, value!)))
            .Select(value => Path.GetRelativePath(repositoryRoot, value).Replace('\\', '/'))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var expected = expectedReferences
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actualReferences);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "YowThi.Erp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Cannot locate the repository root containing YowThi.Erp.slnx.");
    }
}
