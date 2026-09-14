using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProposalService.Application.Abstractions;

namespace ProposalService.Infrastructure.Persistence;

public sealed class UnitOfWork(ProposalDbContext context, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    public async Task<CommitOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return CommitOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning(new EventId(102, "ProposalConcurrencyConflict"), "Proposal update rejected with {Outcome}", "proposal_concurrency_conflict");
            return CommitOutcome.ConcurrencyConflict;
        }
    }
}
