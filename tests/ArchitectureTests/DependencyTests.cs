using System.Reflection;
using System.Xml.Linq;

namespace ArchitectureTests;

public sealed class DependencyTests
{
    public static TheoryData<string, string> ServiceLayers => new()
    {
        { "ProposalService", "Domain" },
        { "ProposalService", "Application" },
        { "ProposalService", "Infrastructure" },
        { "ProposalService", "Api" },
        { "ContractService", "Domain" },
        { "ContractService", "Application" },
        { "ContractService", "Infrastructure" },
        { "ContractService", "Api" }
    };

    [Theory]
    [MemberData(nameof(ServiceLayers))]
    public void Assembly_References_RespectLayerAndServiceBoundaries(string service, string layer)
    {
        var assembly = Assembly.Load($"{service}.{layer}");
        var allowedProjects = AllowedProjectNames(service, layer);

        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            var name = Assert.IsType<string>(reference.Name);
            if (IsServiceAssembly(name))
            {
                // API can use its own domain through the documented transitive graph.
                Assert.Contains(name, layer == "Api" ? [.. allowedProjects, $"{service}.Domain"] : allowedProjects);
            }

            if (layer is "Domain" or "Application")
            {
                Assert.False(IsInfrastructureDependency(name), $"{assembly.GetName().Name} references {name}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ServiceLayers))]
    public void Project_DeclaredReferences_MatchExactDependencyGraph(string service, string layer)
    {
        var project = ReadProject(service, layer);
        var references = project.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(Assert.IsType<string>(element.Attribute("Include")?.Value).Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedProjectNames(service, layer).Order(StringComparer.Ordinal), references);
    }

    [Theory]
    [InlineData("ProposalService", "Domain")]
    [InlineData("ProposalService", "Application")]
    [InlineData("ContractService", "Domain")]
    [InlineData("ContractService", "Application")]
    public void InnerProject_PackageAndFrameworkReferences_ExcludeInfrastructure(string service, string layer)
    {
        var project = ReadProject(service, layer);
        var references = project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "FrameworkReference")
            .Select(element => Assert.IsType<string>((element.Attribute("Include") ?? element.Attribute("Update"))?.Value));

        Assert.All(references, name => Assert.False(IsInfrastructureDependency(name), $"{service}.{layer} references {name}."));
        Assert.Equal("Microsoft.NET.Sdk", project.Root?.Attribute("Sdk")?.Value);
    }

    private static string[] AllowedProjectNames(string service, string layer) => layer switch
    {
        "Domain" => [],
        "Application" => [$"{service}.Domain"],
        "Infrastructure" => [$"{service}.Application", $"{service}.Domain"],
        "Api" => [$"{service}.Application", $"{service}.Infrastructure"],
        _ => throw new ArgumentOutOfRangeException(nameof(layer))
    };

    private static bool IsServiceAssembly(string name) =>
        name.StartsWith("ProposalService.", StringComparison.Ordinal) ||
        name.StartsWith("ContractService.", StringComparison.Ordinal);

    private static bool IsInfrastructureDependency(string name) =>
        name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase);

    private static XDocument ReadProject(string service, string layer)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InsurancePlatform.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine(directory.FullName, "src", service, $"{service}.{layer}", $"{service}.{layer}.csproj"));
    }
}
