using ContractService.Application.Abstractions;
using ContractService.Domain.Contracts;

namespace ContractService.Application.Contracts;

public sealed class CreateContract(IContractRepository repository, IProposalGateway gateway, IUnitOfWork unitOfWork, IClock clock) : ICreateContractUseCase
{
    public async Task<Result<ContractOutput>> ExecuteAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (proposalId == Guid.Empty) return Result<ContractOutput>.Failure(ContractError.ValidationFailed);
        if (await repository.GetByProposalIdAsync(proposalId, cancellationToken) is not null)
            return Result<ContractOutput>.Failure(ContractError.ContractAlreadyExists);

        var eligibility = await gateway.GetEligibilityAsync(proposalId, cancellationToken);
        ContractError? error = eligibility switch
        {
            ProposalEligibilityResult.Approved => null,
            ProposalEligibilityResult.UnderReview or ProposalEligibilityResult.Rejected => ContractError.ProposalNotApproved,
            ProposalEligibilityResult.NotFound => ContractError.ProposalNotFound,
            ProposalEligibilityResult.Unavailable => ContractError.ProposalServiceUnavailable,
            _ => ContractError.ProposalServiceInvalidResponse
        };
        if (error is not null) return Result<ContractOutput>.Failure(error.Value);

        var contract = Contract.Create(proposalId, clock.UtcNow);
        await repository.AddAsync(contract, cancellationToken);
        var outcome = await unitOfWork.SaveChangesAsync(cancellationToken);
        return outcome == CommitOutcome.ContractAlreadyExists
            ? Result<ContractOutput>.Failure(ContractError.ContractAlreadyExists)
            : Result<ContractOutput>.Success(ContractOutput.From(contract));
    }
}
