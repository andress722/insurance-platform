using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContractService.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ContractService.Infrastructure.ProposalApi;

public sealed class ProposalHttpGateway(HttpClient client, ILogger<ProposalHttpGateway> logger) : IProposalGateway
{
    public async Task<ProposalEligibilityResult> GetEligibilityAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = await ReadEligibilityAsync(proposalId, cancellationToken);
        logger.LogInformation(new EventId(3101, "ProposalEligibility"), "Proposal {ProposalId} eligibility {Outcome} in {DurationMs} ms", proposalId, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return outcome;
    }

    private async Task<ProposalEligibilityResult> ReadEligibilityAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync($"/api/v1/proposals/{proposalId:D}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return ProposalEligibilityResult.NotFound;
            if ((int)response.StatusCode >= 500) return ProposalEligibilityResult.Unavailable;
            if (!response.IsSuccessStatusCode) return ProposalEligibilityResult.InvalidResponse;
            var proposal = await response.Content.ReadFromJsonAsync<RemoteProposal>(cancellationToken);
            if (proposal is null || proposal.Id != proposalId || proposal.Id == Guid.Empty) return ProposalEligibilityResult.InvalidResponse;
            return proposal.Status switch
            {
                "approved" => ProposalEligibilityResult.Approved,
                "under_review" => ProposalEligibilityResult.UnderReview,
                "rejected" => ProposalEligibilityResult.Rejected,
                _ => ProposalEligibilityResult.InvalidResponse
            };
        }
        catch (JsonException) { return ProposalEligibilityResult.InvalidResponse; }
        catch (NotSupportedException) { return ProposalEligibilityResult.InvalidResponse; }
        catch (HttpRequestException) { return ProposalEligibilityResult.Unavailable; }
        catch (BrokenCircuitException) { return ProposalEligibilityResult.Unavailable; }
        catch (TimeoutRejectedException) when (!cancellationToken.IsCancellationRequested) { return ProposalEligibilityResult.Unavailable; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return ProposalEligibilityResult.Unavailable; }
    }

    private sealed record RemoteProposal(Guid Id, string? Status);
}
