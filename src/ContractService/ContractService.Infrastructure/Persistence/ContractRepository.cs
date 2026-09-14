using ContractService.Application.Abstractions;
using ContractService.Domain.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ContractService.Infrastructure.Persistence;

public sealed class ContractRepository(ContractDbContext context) : IContractRepository
{
    public async Task AddAsync(Contract contract, CancellationToken cancellationToken) => await context.Contracts.AddAsync(contract, cancellationToken);
    public Task<Contract?> GetByIdAsync(ContractId id, CancellationToken cancellationToken) => context.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<Contract?> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken) => context.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.ProposalId == proposalId, cancellationToken);
}

public sealed class UnitOfWork(ContractDbContext context, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    public async Task<CommitOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return CommitOutcome.Saved;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_contracts_proposal_id" })
        {
            logger.LogWarning(new EventId(3102, "ContractConflict"), "Contract insertion rejected with {Outcome}", "contract_already_exists");
            context.ChangeTracker.Clear();
            return CommitOutcome.ContractAlreadyExists;
        }
    }
}
