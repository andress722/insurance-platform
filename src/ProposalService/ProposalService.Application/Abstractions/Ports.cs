using ProposalService.Application.Proposals;
using ProposalService.Domain.Proposals;

namespace ProposalService.Application.Abstractions;

public interface IClock { DateTimeOffset UtcNow { get; } }
public enum CommitOutcome { Saved, ConcurrencyConflict }
public interface IUnitOfWork { Task<CommitOutcome> SaveChangesAsync(CancellationToken cancellationToken); }
public interface IProposalRepository
{
    Task AddAsync(Proposal proposal, CancellationToken cancellationToken);

    /// <summary>Reads the current state of a proposal without enlisting it in the running transaction.</summary>
    Task<Proposal?> GetByIdAsync(ProposalId id, CancellationToken cancellationToken);

    /// <summary>Loads the aggregate so a decision can be applied and committed by <see cref="IUnitOfWork"/>.</summary>
    Task<Proposal?> GetForDecisionAsync(ProposalId id, CancellationToken cancellationToken);

    Task<Page<Proposal>> ListAsync(ProposalStatus? status, int page, int pageSize, CancellationToken cancellationToken);
}
