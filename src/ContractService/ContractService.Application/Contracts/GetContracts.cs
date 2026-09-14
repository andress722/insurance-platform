using ContractService.Application.Abstractions;
using ContractService.Domain.Contracts;

namespace ContractService.Application.Contracts;

public sealed class GetContractById(IContractRepository repository) : IGetContractByIdUseCase
{
    public async Task<Result<ContractOutput>> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return Result<ContractOutput>.Failure(ContractError.ValidationFailed);
        var contract = await repository.GetByIdAsync(new ContractId(id), cancellationToken);
        return contract is null ? Result<ContractOutput>.Failure(ContractError.ContractNotFound) : Result<ContractOutput>.Success(ContractOutput.From(contract));
    }
}

public sealed class GetContractByProposal(IContractRepository repository) : IGetContractByProposalUseCase
{
    public async Task<Result<ContractOutput>> ExecuteAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        if (proposalId == Guid.Empty) return Result<ContractOutput>.Failure(ContractError.ValidationFailed);
        var contract = await repository.GetByProposalIdAsync(proposalId, cancellationToken);
        return contract is null ? Result<ContractOutput>.Failure(ContractError.ContractNotFound) : Result<ContractOutput>.Success(ContractOutput.From(contract));
    }
}
