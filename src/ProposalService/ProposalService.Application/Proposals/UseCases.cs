using ProposalService.Application.Abstractions;
using ProposalService.Domain.Proposals;

namespace ProposalService.Application.Proposals;

public sealed class CreateProposalUseCase(IProposalRepository repository, IUnitOfWork unitOfWork, IClock clock) : ICreateProposalUseCase
{
    public async Task<Result<ProposalOutput>> ExecuteAsync(CreateProposalCommand command, CancellationToken cancellationToken)
    {
        Proposal proposal;
        try { proposal = Proposal.Create(command.CustomerId, command.ProductCode, command.InsuredAmount, command.MonthlyPremium, clock.UtcNow); }
        catch (ProposalRuleException error) { return Result<ProposalOutput>.Failure(error.Code, error.Message, error.Field); }
        await repository.AddAsync(proposal, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ProposalOutput>.Success(ProposalOutput.From(proposal));
    }
}

public sealed class GetProposalByIdUseCase(IProposalRepository repository) : IGetProposalByIdUseCase
{
    public async Task<Result<ProposalOutput>> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return Result<ProposalOutput>.Failure("validation_failed", "A nonempty UUID is required.", "id");
        var proposal = await repository.GetByIdAsync(new ProposalId(id), cancellationToken);
        return proposal is null ? Result<ProposalOutput>.Failure("proposal_not_found", "The proposal does not exist.")
            : Result<ProposalOutput>.Success(ProposalOutput.From(proposal));
    }
}

public sealed class ListProposalsUseCase(IProposalRepository repository) : IListProposalsUseCase
{
    public async Task<Result<ProposalPage>> ExecuteAsync(ProposalStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue || (status.HasValue && !Enum.IsDefined(status.Value)))
            return Result<ProposalPage>.Failure("validation_failed", "The status or pagination is invalid.", "query");
        var result = await repository.ListAsync(status, page, pageSize, cancellationToken);
        return Result<ProposalPage>.Success(new(result.Items.Select(ProposalOutput.From).ToArray(), page, pageSize,
            result.TotalItems, (long)Math.Ceiling((decimal)result.TotalItems / pageSize)));
    }
}

public sealed class ChangeProposalStatusUseCase(IProposalRepository repository, IUnitOfWork unitOfWork, IClock clock) : IChangeProposalStatusUseCase
{
    public async Task<Result<ProposalOutput>> ExecuteAsync(Guid id, ProposalStatus status, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || status is not (ProposalStatus.Approved or ProposalStatus.Rejected))
            return Result<ProposalOutput>.Failure("validation_failed", "A nonempty UUID and a final status are required.", "status");
        var key = new ProposalId(id);
        var proposal = await repository.GetForDecisionAsync(key, cancellationToken);
        if (proposal is null) return Result<ProposalOutput>.Failure("proposal_not_found", "The proposal does not exist.");
        if (proposal.Status == status) return Result<ProposalOutput>.Success(ProposalOutput.From(proposal));
        try
        {
            if (status == ProposalStatus.Approved) proposal.Approve(clock.UtcNow);
            else proposal.Reject(clock.UtcNow);
        }
        catch (ProposalRuleException error) { return Result<ProposalOutput>.Failure(error.Code, error.Message); }
        if (await unitOfWork.SaveChangesAsync(cancellationToken) == CommitOutcome.ConcurrencyConflict)
        {
            proposal = await repository.GetByIdAsync(key, cancellationToken);
            if (proposal?.Status != status)
                return Result<ProposalOutput>.Failure("proposal_concurrency_conflict", "Another decision was saved concurrently.");
        }
        return Result<ProposalOutput>.Success(ProposalOutput.From(proposal));
    }
}
