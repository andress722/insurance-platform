using Microsoft.EntityFrameworkCore;
using ProposalService.Application.Abstractions;
using ProposalService.Application.Proposals;
using ProposalService.Domain.Proposals;

namespace ProposalService.Infrastructure.Persistence;

public sealed class ProposalRepository(ProposalDbContext context) : IProposalRepository
{
    public async Task AddAsync(Proposal proposal, CancellationToken cancellationToken) => await context.Proposals.AddAsync(proposal, cancellationToken);
    public Task<Proposal?> GetByIdAsync(ProposalId id, CancellationToken cancellationToken) => context.Proposals.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    public Task<Proposal?> GetForDecisionAsync(ProposalId id, CancellationToken cancellationToken) => context.Proposals.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    public async Task<Page<Proposal>> ListAsync(ProposalStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Proposals.AsNoTracking();
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        var count = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new(items, page, pageSize, count);
    }
}
