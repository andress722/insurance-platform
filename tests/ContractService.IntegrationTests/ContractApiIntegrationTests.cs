using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ContractService.Application.Abstractions;
using ContractService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace ContractService.IntegrationTests;

public sealed class ContractApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("contracts_test_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly StubProposalGateway _stubGateway = new();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Configuration stays scoped to this factory; mutating process environment variables would leak across fixtures.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:ContractDb", _postgres.GetConnectionString());
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("ProposalApi:BaseUrl", "http://localhost:5000");
            builder.UseSetting("ProposalApi:TimeoutSeconds", "2");
            builder.UseSetting("ProposalApi:AttemptTimeoutSeconds", "0.8");
            builder.UseSetting("ProposalApi:RetryCount", "1");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProposalGateway>();
                services.AddSingleton<IProposalGateway>(_stubGateway);
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractDbContext>();
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
    public async Task CreateContract_WhenProposalApproved_Returns201_WithLocation_AndPersists()
    {
        var proposalId = Guid.NewGuid();
        _stubGateway.SetEligibility(proposalId, ProposalEligibilityResult.Approved);

        var response = await _client.PostAsJsonAsync("/api/v1/contracts", new { proposalId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location;
        Assert.NotNull(location);

        var contract = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(contract);
        var id = contract["id"]?.GetValue<string>();
        Assert.NotNull(id);
        Assert.Contains(id, location.ToString());
        Assert.Equal(proposalId.ToString(), contract["proposalId"]?.GetValue<string>());
        Assert.NotNull(contract["contractedAtUtc"]);

        // Query by ID
        var getByIdRes = await _client.GetAsync($"/api/v1/contracts/{id}");
        Assert.Equal(HttpStatusCode.OK, getByIdRes.StatusCode);

        // Query by proposal
        var getByPropRes = await _client.GetAsync($"/api/v1/contracts/by-proposal/{proposalId}");
        Assert.Equal(HttpStatusCode.OK, getByPropRes.StatusCode);
    }

    [Theory]
    [InlineData(ProposalEligibilityResult.UnderReview, HttpStatusCode.Conflict, "proposal_not_approved")]
    [InlineData(ProposalEligibilityResult.Rejected, HttpStatusCode.Conflict, "proposal_not_approved")]
    [InlineData(ProposalEligibilityResult.NotFound, HttpStatusCode.NotFound, "proposal_not_found")]
    [InlineData(ProposalEligibilityResult.Unavailable, HttpStatusCode.ServiceUnavailable, "proposal_service_unavailable")]
    [InlineData(ProposalEligibilityResult.InvalidResponse, HttpStatusCode.BadGateway, "proposal_service_invalid_response")]
    public async Task CreateContract_WhenProposalNotEligibleOrFails_ReturnsExpectedStatusCode_AndDoesNotPersist(
        ProposalEligibilityResult remoteStatus, HttpStatusCode expectedHttp, string expectedCode)
    {
        var proposalId = Guid.NewGuid();
        _stubGateway.SetEligibility(proposalId, remoteStatus);

        var response = await _client.PostAsJsonAsync("/api/v1/contracts", new { proposalId });
        Assert.Equal(expectedHttp, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, problem["code"]?.GetValue<string>());
        Assert.NotNull(problem["traceId"]);

        // Verify nothing was persisted
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractDbContext>();
        var persisted = await db.Contracts.AnyAsync(c => c.ProposalId == proposalId);
        Assert.False(persisted);
    }

    [Fact]
    public async Task CreateContract_WhenContractAlreadyExists_Returns409_ContractAlreadyExists()
    {
        var proposalId = Guid.NewGuid();
        _stubGateway.SetEligibility(proposalId, ProposalEligibilityResult.Approved);

        // First creation succeeds
        var firstResponse = await _client.PostAsJsonAsync("/api/v1/contracts", new { proposalId });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Second creation returns 409
        var secondResponse = await _client.PostAsJsonAsync("/api/v1/contracts", new { proposalId });
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var problem = await secondResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(problem);
        Assert.Equal("contract_already_exists", problem["code"]?.GetValue<string>());
    }

    [Fact]
    public async Task ConcurrentCreations_ForSameProposal_ResultsInExactlyOneSuccessAndOneConflict()
    {
        var proposalId = Guid.NewGuid();
        _stubGateway.SetEligibility(proposalId, ProposalEligibilityResult.Approved);

        var task1 = _client.PostAsJsonAsync("/api/v1/contracts", new { proposalId });
        var task2 = _client.PostAsJsonAsync("/api/v1/contracts", new { proposalId });

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        Assert.Contains(HttpStatusCode.Created, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractDbContext>();
        var count = await db.Contracts.CountAsync(c => c.ProposalId == proposalId);
        Assert.Equal(1, count);
    }

    private sealed class StubProposalGateway : IProposalGateway
    {
        private readonly Dictionary<Guid, ProposalEligibilityResult> _eligibilities = [];

        public void SetEligibility(Guid proposalId, ProposalEligibilityResult result) =>
            _eligibilities[proposalId] = result;

        public Task<ProposalEligibilityResult> GetEligibilityAsync(Guid proposalId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_eligibilities.GetValueOrDefault(proposalId, ProposalEligibilityResult.NotFound));
        }
    }
}
