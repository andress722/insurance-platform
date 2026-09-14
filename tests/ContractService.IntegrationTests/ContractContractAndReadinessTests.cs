using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ContractService.IntegrationTests;

/// <summary>
/// Guards the published v1 contract (docs/04) without a database: the OpenAPI document, the problem codes of
/// failures produced by the pipeline itself and the readiness semantics when PostgreSQL is unreachable.
/// </summary>
public sealed class ContractContractAndReadinessTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        // Unroutable database on purpose: no endpoint used here touches it, and readiness must report it as down.
        builder.UseSetting("ConnectionStrings:ContractDb", "Host=127.0.0.1;Port=1;Database=contracts;Username=postgres;Password=postgres;Timeout=1;Command Timeout=1");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("ProposalApi:BaseUrl", "http://proposal-api:8080");
    });

    [Fact]
    public async Task OpenApi_DocumentsExactlyTheEndpointsOfTheV1Contract()
    {
        using var client = _factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonObject>("/swagger/v1/swagger.json");

        Assert.NotNull(document);
        Assert.Equal("v1", document["info"]?["version"]?.GetValue<string>());

        var paths = document["paths"]!.AsObject();
        Assert.Equal(
            new[] { "/api/v1/contracts", "/api/v1/contracts/by-proposal/{proposalId}", "/api/v1/contracts/{id}" },
            paths.Select(path => path.Key).Order(StringComparer.Ordinal));

        AssertResponses(paths, "/api/v1/contracts", "post", "201", "400", "404", "409", "502", "503");
        AssertResponses(paths, "/api/v1/contracts/{id}", "get", "200", "400", "404");
        AssertResponses(paths, "/api/v1/contracts/by-proposal/{proposalId}", "get", "200", "400", "404");
    }

    [Fact]
    public async Task PipelineFailures_KeepTheirOwnCause_InsteadOfBeingReportedAsInternalError()
    {
        using var client = _factory.CreateClient();

        var unknownRoute = await client.GetAsync("/api/v1/unknown");
        Assert.Equal(HttpStatusCode.NotFound, unknownRoute.StatusCode);

        var methodNotAllowed = await client.PutAsJsonAsync("/api/v1/contracts", new { proposalId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, methodNotAllowed.StatusCode);

        var problem = await methodNotAllowed.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(problem);
        Assert.Equal("method_not_allowed", problem["code"]?.GetValue<string>());
        Assert.NotNull(problem["traceId"]);
        Assert.NotEqual("An unexpected error occurred.", problem["detail"]?.GetValue<string>());
    }

    [Fact]
    public async Task Readiness_ReportsUnhealthy_WhenTheDatabaseIsUnreachable_ButLivenessStaysHealthy()
    {
        using var client = _factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);

        var body = await ready.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertResponses(JsonObject paths, string path, string method, params string[] expected)
    {
        var responses = paths[path]![method]!["responses"]!.AsObject().Select(response => response.Key).Order(StringComparer.Ordinal);
        Assert.Equal(expected.Order(StringComparer.Ordinal), responses);
    }

    public void Dispose() => _factory.Dispose();
}
