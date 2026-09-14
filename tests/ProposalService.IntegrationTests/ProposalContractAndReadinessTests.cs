using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ProposalService.Api.Contracts;
using ProposalService.Domain.Proposals;

namespace ProposalService.IntegrationTests;

/// <summary>
/// Guards the published v1 contract (docs/04) without a database: the OpenAPI document, the wire vocabulary
/// of the status field and the readiness semantics when PostgreSQL is unreachable.
/// </summary>
public sealed class ProposalContractAndReadinessTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        // Unroutable database on purpose: no endpoint used here touches it, and readiness must report it as down.
        builder.UseSetting("ConnectionStrings:ProposalDb", "Host=127.0.0.1;Port=1;Database=proposals;Username=postgres;Password=postgres;Timeout=1;Command Timeout=1");
        builder.UseSetting("Database:MigrateOnStartup", "false");
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
            new[] { "/api/v1/proposals", "/api/v1/proposals/{id}", "/api/v1/proposals/{id}/status" },
            paths.Select(path => path.Key).Order(StringComparer.Ordinal));

        AssertResponses(paths, "/api/v1/proposals", "post", "201", "400", "500");
        AssertResponses(paths, "/api/v1/proposals", "get", "200", "400", "500");
        AssertResponses(paths, "/api/v1/proposals/{id}", "get", "200", "400", "404", "500");
        AssertResponses(paths, "/api/v1/proposals/{id}/status", "patch", "200", "400", "404", "409", "500");
    }

    [Fact]
    public async Task OpenApi_SerializesTheThreeStatusValuesExactlyAsDocumented()
    {
        using var client = _factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonObject>("/swagger/v1/swagger.json");
        var status = document!["components"]!["schemas"]!["ProposalResponse"]!["properties"]!["status"]!;

        Assert.Equal("string", status["type"]?.GetValue<string>());
        Assert.Equal(
            new[] { "approved", "rejected", "under_review" },
            status["enum"]!.AsArray().Select(value => value!.GetValue<string>()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WireVocabulary_CoversEveryDomainStatus_AndNeverChangesSilently()
    {
        var wireValues = Enum.GetValues<ProposalStatus>().Select(ProposalStatusNames.ToWire).Order(StringComparer.Ordinal);

        Assert.Equal(new[] { "approved", "rejected", "under_review" }, wireValues);
        Assert.True(ProposalStatusNames.TryParse("under_review", out var parsed) && parsed == ProposalStatus.UnderReview);
        Assert.False(ProposalStatusNames.TryParse("UnderReview", out _));
        Assert.False(ProposalStatusNames.TryParse(null, out _));
    }

    [Fact]
    public async Task PipelineFailures_KeepTheirOwnCause_InsteadOfBeingReportedAsInternalError()
    {
        using var client = _factory.CreateClient();

        var methodNotAllowed = await client.PutAsJsonAsync("/api/v1/proposals", new { status = "approved" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, methodNotAllowed.StatusCode);

        var problem = await methodNotAllowed.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(problem);
        Assert.Equal("method_not_allowed", problem["code"]?.GetValue<string>());
        Assert.NotNull(problem["traceId"]);
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
