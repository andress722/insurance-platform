using System.Reflection;

namespace ArchitectureTests;

public sealed class AdapterPlacementTests
{
    [Theory]
    [InlineData("ProposalService")]
    [InlineData("ContractService")]
    public void UseCases_ImplementInputPorts_OnlyInApplication(string service)
    {
        var types = LoadServiceTypes(service);
        var ports = types.Where(type => type.IsInterface && type.Name.EndsWith("UseCase", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(ports);
        foreach (var port in ports)
        {
            Assert.Equal($"{service}.Application", port.Assembly.GetName().Name);
            var implementations = types.Where(type => type is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(type)).ToArray();
            Assert.NotEmpty(implementations);
            Assert.All(implementations, type => Assert.Equal($"{service}.Application", type.Assembly.GetName().Name));
        }
    }

    [Theory]
    [InlineData("ProposalService", "IProposalRepository")]
    [InlineData("ContractService", "IContractRepository")]
    public void Repositories_ImplementOutputPorts_OnlyInInfrastructure(string service, string portName)
    {
        var types = LoadServiceTypes(service);
        var port = Assert.Single(types, type => type.IsInterface && type.Name == portName);

        Assert.Equal($"{service}.Application", port.Assembly.GetName().Name);
        var implementations = types.Where(type => type is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(type)).ToArray();
        Assert.NotEmpty(implementations);
        Assert.All(implementations, type => Assert.Equal($"{service}.Infrastructure", type.Assembly.GetName().Name));
    }

    [Theory]
    [InlineData("ProposalService")]
    [InlineData("ContractService")]
    public void Controllers_DependOnUseCases_WithoutPersistenceAdapters(string service)
    {
        var controllers = Assembly.Load($"{service}.Api").GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        {
            var dependencies = controller.GetConstructors().SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType))
                .Concat(controller.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Select(field => field.FieldType));

            Assert.All(dependencies, type =>
            {
                Assert.NotEqual($"{service}.Infrastructure", type.Assembly.GetName().Name);
                Assert.False(type.Name.Contains("Repository", StringComparison.Ordinal), $"{controller.Name} depends on {type.Name}.");
                Assert.False(type.Name.Contains("DbContext", StringComparison.Ordinal), $"{controller.Name} depends on {type.Name}.");
                Assert.False(type.Name.Contains("UnitOfWork", StringComparison.Ordinal), $"{controller.Name} depends on {type.Name}.");
            });
        }
    }

    private static Type[] LoadServiceTypes(string service) =>
        new[] { "Domain", "Application", "Infrastructure", "Api" }
            .SelectMany(layer => Assembly.Load($"{service}.{layer}").GetTypes())
            .ToArray();
}
