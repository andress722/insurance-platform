using ContractService.Domain.Contracts;

namespace ContractService.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IContractRepository
{
    Task AddAsync(Contract contract, CancellationToken cancellationToken);
    Task<Contract?> GetByIdAsync(ContractId id, CancellationToken cancellationToken);
    Task<Contract?> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken);
}

public enum CommitOutcome { Saved, ContractAlreadyExists }

public interface IUnitOfWork
{
    Task<CommitOutcome> SaveChangesAsync(CancellationToken cancellationToken);
}

public enum ProposalEligibilityResult { Approved, UnderReview, Rejected, NotFound, Unavailable, InvalidResponse }

public interface IProposalGateway
{
    Task<ProposalEligibilityResult> GetEligibilityAsync(Guid proposalId, CancellationToken cancellationToken);
}
