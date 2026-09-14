using System.Net;
using System.Text;
using ContractService.Application.Abstractions;
using ContractService.Infrastructure.ProposalApi;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContractService.UnitTests;

public sealed class ProposalHttpGatewayTests
{
    [Fact]
    public async Task GetEligibility_WhenRemoteReturnsApproved_ReturnsApproved()
    {
        var proposalId = Guid.NewGuid();
        var json = $$"""{"id":"{{proposalId}}","status":"approved","customerId":"C-1","extraField":123}""";
        var gateway = CreateGateway(HttpStatusCode.OK, json);

        var result = await gateway.GetEligibilityAsync(proposalId, CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.Approved, result);
    }

    [Fact]
    public async Task GetEligibility_WhenRemoteReturnsUnderReview_ReturnsUnderReview()
    {
        var proposalId = Guid.NewGuid();
        var json = $$"""{"id":"{{proposalId}}","status":"under_review"}""";
        var gateway = CreateGateway(HttpStatusCode.OK, json);

        var result = await gateway.GetEligibilityAsync(proposalId, CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.UnderReview, result);
    }

    [Fact]
    public async Task GetEligibility_WhenRemoteReturnsRejected_ReturnsRejected()
    {
        var proposalId = Guid.NewGuid();
        var json = $$"""{"id":"{{proposalId}}","status":"rejected"}""";
        var gateway = CreateGateway(HttpStatusCode.OK, json);

        var result = await gateway.GetEligibilityAsync(proposalId, CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.Rejected, result);
    }

    [Fact]
    public async Task GetEligibility_WhenRemoteReturns404_ReturnsNotFound()
    {
        var gateway = CreateGateway(HttpStatusCode.NotFound, """{"code":"proposal_not_found"}""");

        var result = await gateway.GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.NotFound, result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetEligibility_WhenRemoteReturns5xx_ReturnsUnavailable(HttpStatusCode statusCode)
    {
        var gateway = CreateGateway(statusCode, "Internal Server Error");

        var result = await gateway.GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.Unavailable, result);
    }

    [Fact]
    public async Task GetEligibility_WhenRemoteIdDiffersFromRequested_ReturnsInvalidResponse()
    {
        var requestedId = Guid.NewGuid();
        var differentId = Guid.NewGuid();
        var json = $$"""{"id":"{{differentId}}","status":"approved"}""";
        var gateway = CreateGateway(HttpStatusCode.OK, json);

        var result = await gateway.GetEligibilityAsync(requestedId, CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.InvalidResponse, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not a json")]
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000000","status":"approved"}""")]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","status":"unknown_status"}""")]
    public async Task GetEligibility_WhenRemotePayloadInvalid_ReturnsInvalidResponse(string payload)
    {
        var proposalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var gateway = CreateGateway(HttpStatusCode.OK, payload);

        var result = await gateway.GetEligibilityAsync(proposalId, CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.InvalidResponse, result);
    }

    [Fact]
    public async Task GetEligibility_WhenNetworkExceptionOccurs_ReturnsUnavailable()
    {
        var handler = new MockHandler((_, _) => throw new HttpRequestException("Connection refused"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var gateway = new ProposalHttpGateway(client, NullLogger<ProposalHttpGateway>.Instance);

        var result = await gateway.GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ProposalEligibilityResult.Unavailable, result);
    }

    [Fact]
    public async Task GetEligibility_WhenCallerCancels_ThrowsOperationCanceledException()
    {
        var handler = new MockHandler(async (_, ct) =>
        {
            await Task.Delay(500, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var gateway = new ProposalHttpGateway(client, NullLogger<ProposalHttpGateway>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.GetEligibilityAsync(Guid.NewGuid(), cts.Token));
    }

    private static ProposalHttpGateway CreateGateway(HttpStatusCode code, string responseContent)
    {
        var handler = new MockHandler((_, _) => Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
        }));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        return new ProposalHttpGateway(client, NullLogger<ProposalHttpGateway>.Instance);
    }

    private sealed class MockHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
