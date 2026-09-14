using ContractService.Application.Contracts;

namespace ContractService.Api.Contracts;

public sealed record CreateContractRequest(Guid ProposalId);

public sealed record ContractResponse(Guid Id, Guid ProposalId, DateTimeOffset ContractedAtUtc)
{
    public static ContractResponse From(ContractOutput output) => new(output.Id, output.ProposalId, output.ContractedAtUtc);
}
