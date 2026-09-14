using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProposalService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ProposalService.IntegrationTests;

public sealed class ProposalApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("proposals_test_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Configuration stays scoped to this factory; mutating process environment variables would leak across fixtures.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:ProposalDb", _postgres.GetConnectionString());
            builder.UseSetting("Database:MigrateOnStartup", "false");
        });

        // Run migrations explicitly on the clean container database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProposalDbContext>();
        await db.Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task HealthEndpoints_ReturnHealthy()
    {
        var liveResponse = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

        var readyResponse = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
    }

    [Fact]
    public async Task CreateProposal_Returns201_WithLocationHeader_AndPreservesPrecision()
    {
        var payload = new
        {
            customerId = "CUST-INT-1",
            productCode = "AUTO_PLUS",
            insuredAmount = 75000.50m,
            monthlyPremium = 199.90m
        };

        var response = await _client.PostAsJsonAsync("/api/v1/proposals", payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location;
        Assert.NotNull(location);

        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(json);
        var id = json["id"]?.GetValue<string>();
        Assert.NotNull(id);
        Assert.Contains(id, location.ToString());
        Assert.Equal("under_review", json["status"]?.GetValue<string>());
        Assert.Equal(75000.50m, json["insuredAmount"]?.GetValue<decimal>());
        Assert.Equal(199.90m, json["monthlyPremium"]?.GetValue<decimal>());
        Assert.Equal(1, json["version"]?.GetValue<int>());
    }

    [Fact]
    public async Task GetProposalById_Returns200_WhenExists_Or404_WhenMissing()
    {
        // Create first
        var createResponse = await _client.PostAsJsonAsync("/api/v1/proposals", new
        {
            customerId = "CUST-GET-1",
            productCode = "LIFE_BASIC",
            insuredAmount = 100000.00m,
            monthlyPremium = 80.00m
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var id = created!["id"]!.GetValue<string>();

        // Get existing
        var getResponse = await _client.GetAsync($"/api/v1/proposals/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(id, fetched!["id"]?.GetValue<string>());
        Assert.Equal("LIFE_BASIC", fetched["productCode"]?.GetValue<string>());

        // Get missing UUID
        var notFoundResponse = await _client.GetAsync($"/api/v1/proposals/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        var notFoundProblem = await notFoundResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("proposal_not_found", notFoundProblem!["code"]?.GetValue<string>());
        Assert.NotNull(notFoundProblem["traceId"]);
    }

    [Fact]
    public async Task ListProposals_ReturnsPaginatedResults_WithFilter()
    {
        var product = $"PROD_{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/v1/proposals", new
        {
            customerId = "CUST-LIST",
            productCode = product,
            insuredAmount = 10000m,
            monthlyPremium = 50m
        });

        var response = await _client.GetAsync("/api/v1/proposals?status=under_review&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(page);
        Assert.True(page["totalItems"]?.GetValue<int>() >= 1);
        Assert.Equal(1, page["page"]?.GetValue<int>());
        Assert.Equal(10, page["pageSize"]?.GetValue<int>());
        Assert.NotNull(page["items"]);
    }

    [Fact]
    public async Task ChangeStatus_ApproveAndReject_HandlesLifecycleAndConcurrency()
    {
        // 1. Create proposal
        var createRes = await _client.PostAsJsonAsync("/api/v1/proposals", new
        {
            customerId = "CUST-DECIDE",
            productCode = "AUTO_STD",
            insuredAmount = 30000m,
            monthlyPremium = 100m
        });
        var created = await createRes.Content.ReadFromJsonAsync<JsonObject>();
        var id = created!["id"]!.GetValue<string>();

        // 2. Approve
        var approveRes = await _client.PatchAsJsonAsync($"/api/v1/proposals/{id}/status", new { status = "approved" });
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);
        var approved = await approveRes.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("approved", approved!["status"]?.GetValue<string>());
        Assert.Equal(2, approved["version"]?.GetValue<int>());

        // 3. Repeat approve (idempotent)
        var repeatRes = await _client.PatchAsJsonAsync($"/api/v1/proposals/{id}/status", new { status = "approved" });
        Assert.Equal(HttpStatusCode.OK, repeatRes.StatusCode);
        var repeated = await repeatRes.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("approved", repeated!["status"]?.GetValue<string>());
        Assert.Equal(2, repeated["version"]?.GetValue<int>());

        // 4. Try to reject an already approved proposal (terminal transition forbidden)
        var rejectRes = await _client.PatchAsJsonAsync($"/api/v1/proposals/{id}/status", new { status = "rejected" });
        Assert.Equal(HttpStatusCode.Conflict, rejectRes.StatusCode);
        var problem = await rejectRes.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("invalid_status_transition", problem!["code"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateProposal_WithInvalidData_Returns400_ProblemDetails()
    {
        var invalidPayload = new
        {
            customerId = "",
            productCode = "AUTO",
            insuredAmount = -50m,
            monthlyPremium = 0m
        };

        var response = await _client.PostAsJsonAsync("/api/v1/proposals", invalidPayload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(problem);
        Assert.Equal("validation_failed", problem["code"]?.GetValue<string>());
        Assert.NotNull(problem["traceId"]);
    }

    /// <summary>
    /// docs/04 section 1: errors travel as RFC 7807 in <c>application/problem+json</c>. A client that negotiates on
    /// the media type cannot recognise the payload when it is served as plain <c>application/json</c>, so the header
    /// is part of the contract and not a detail. Covers an application error, a validation error and a failure raised
    /// by the pipeline itself, which never reaches the controller.
    /// </summary>
    [Fact]
    public async Task ErrorResponses_AreServedAsProblemJson_ForApplicationValidationAndPipelineFailures()
    {
        var notFound = await _client.GetAsync($"/api/v1/proposals/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal("application/problem+json", notFound.Content.Headers.ContentType?.MediaType);

        var invalid = await _client.PostAsJsonAsync("/api/v1/proposals", new { customerId = "", productCode = "A", insuredAmount = -1m, monthlyPremium = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);

        var notAllowed = await _client.PutAsJsonAsync("/api/v1/proposals", new { status = "approved" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, notAllowed.StatusCode);
        Assert.Equal("application/problem+json", notAllowed.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// docs/04 section 2.1 publishes <c>Location</c> as a path, not an absolute URL. An absolute value leaks the
    /// host the service happens to answer on, which behind a proxy or gateway is not the host the client called.
    /// </summary>
    [Fact]
    public async Task CreatedResponse_PublishesARelativeLocation_AsDocumented()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/proposals", new
        {
            customerId = "CUST-LOCATION",
            productCode = "AUTO_BASIC",
            insuredAmount = 1000m,
            monthlyPremium = 10m
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var id = (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.False(location.IsAbsoluteUri);
        Assert.Equal($"/api/v1/proposals/{id}", location.ToString());
    }

    /// <summary>
    /// The timestamp a write returns must be the timestamp a later read returns. The clock has tick resolution and
    /// <c>timestamptz</c> keeps microseconds, so an untruncated value is echoed at full precision by the write and
    /// comes back rounded from the database, and a client comparing the two sees a difference that never happened.
    /// The wire format is asserted too: docs/04 documents the 'Z' designator, not a '+00:00' offset.
    /// </summary>
    [Fact]
    public async Task Timestamps_SurviveTheRoundTripUnchanged_AndUseTheDocumentedZDesignator()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/proposals", new
        {
            customerId = "CUST-TIMESTAMP",
            productCode = "AUTO_BASIC",
            insuredAmount = 1000m,
            monthlyPremium = 10m
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var id = created!["id"]!.GetValue<string>();
        var createdAt = created["createdAtUtc"]!.GetValue<string>();

        Assert.EndsWith("Z", createdAt, StringComparison.Ordinal);
        Assert.DoesNotContain("+00:00", createdAt, StringComparison.Ordinal);

        var afterRead = await (await _client.GetAsync($"/api/v1/proposals/{id}")).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(createdAt, afterRead!["createdAtUtc"]!.GetValue<string>());

        var decided = await (await _client.PatchAsJsonAsync($"/api/v1/proposals/{id}/status", new { status = "approved" })).Content.ReadFromJsonAsync<JsonObject>();
        var decidedAt = decided!["updatedAtUtc"]!.GetValue<string>();
        Assert.EndsWith("Z", decidedAt, StringComparison.Ordinal);

        var afterDecision = await (await _client.GetAsync($"/api/v1/proposals/{id}")).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(decidedAt, afterDecision!["updatedAtUtc"]!.GetValue<string>());
    }
}
