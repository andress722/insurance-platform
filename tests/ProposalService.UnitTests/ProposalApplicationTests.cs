using ProposalService.Application.Abstractions;
using ProposalService.Application.Proposals;
using ProposalService.Domain.Proposals;

namespace ProposalService.UnitTests;

public sealed class ProposalApplicationTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 14, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PA_01_CreateProposal_PersistsOnceAndSavesChanges()
    {
        var repository = new FakeProposalRepository();
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);
        var useCase = new CreateProposalUseCase(repository, uow, clock);

        var command = new CreateProposalCommand("CUST-10", "HOME_SAFE", 100000m, 250m);
        var result = await useCase.ExecuteAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("CUST-10", result.Value.CustomerId);
        Assert.Equal("HOME_SAFE", result.Value.ProductCode);
        Assert.Equal(ProposalStatus.UnderReview, result.Value.Status);
        Assert.Single(repository.AddedProposals);
        Assert.Equal(1, uow.SaveChangesCalls);
    }

    [Fact]
    public async Task PA_02_GetProposalById_WhenNotFound_ReturnsProposalNotFound()
    {
        var repository = new FakeProposalRepository();
        var useCase = new GetProposalByIdUseCase(repository);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("proposal_not_found", result.Error?.Code);
    }

    [Fact]
    public async Task PA_02_GetProposalById_WhenEmptyGuid_ReturnsValidationFailed()
    {
        var repository = new FakeProposalRepository();
        var useCase = new GetProposalByIdUseCase(repository);

        var result = await useCase.ExecuteAsync(Guid.Empty, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation_failed", result.Error?.Code);
    }

    [Fact]
    public async Task PA_03_ListProposals_PropagatesFilterAndReturnsEnvelope()
    {
        var repository = new FakeProposalRepository();
        var p1 = Proposal.Create("C1", "P1", 1000m, 10m, FixedNow);
        var p2 = Proposal.Create("C2", "P2", 2000m, 20m, FixedNow);
        repository.StoredProposals[p1.Id] = p1;
        repository.StoredProposals[p2.Id] = p2;

        var useCase = new ListProposalsUseCase(repository);
        var result = await useCase.ExecuteAsync(ProposalStatus.UnderReview, 1, 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.TotalItems);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(10, result.Value.PageSize);
        Assert.Equal(1, result.Value.TotalPages);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public async Task PA_03_ListProposals_InvalidPagination_ReturnsValidationFailed()
    {
        var repository = new FakeProposalRepository();
        var useCase = new ListProposalsUseCase(repository);

        var result = await useCase.ExecuteAsync(null, 0, 20, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("validation_failed", result.Error?.Code);

        var result2 = await useCase.ExecuteAsync(null, 1, 101, CancellationToken.None);
        Assert.False(result2.IsSuccess);
        Assert.Equal("validation_failed", result2.Error?.Code);
    }

    [Fact]
    public async Task PA_04_ChangeStatus_WhenNotFound_ReturnsProposalNotFoundAndDoesNotSave()
    {
        var repository = new FakeProposalRepository();
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);
        var useCase = new ChangeProposalStatusUseCase(repository, uow, clock);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), ProposalStatus.Approved, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("proposal_not_found", result.Error?.Code);
        Assert.Equal(0, uow.SaveChangesCalls);
    }

    [Fact]
    public async Task PA_05_ChangeStatus_WhenValid_UpdatesAggregateAndSavesChanges()
    {
        var repository = new FakeProposalRepository();
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);
        var proposal = Proposal.Create("CUST-1", "AUTO_BASIC", 1000m, 50m, FixedNow);
        repository.StoredProposals[proposal.Id] = proposal;

        var useCase = new ChangeProposalStatusUseCase(repository, uow, clock);
        var result = await useCase.ExecuteAsync(proposal.Id.Value, ProposalStatus.Approved, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(ProposalStatus.Approved, result.Value.Status);
        Assert.Equal(2, result.Value.Version);
        Assert.Equal(1, uow.SaveChangesCalls);
    }

    [Fact]
    public async Task PA_06_ChangeStatus_ConcurrencyConflictWithOppositeDecision_ReturnsConflict()
    {
        var repository = new FakeProposalRepository();
        var uow = new FakeUnitOfWork { ReturnConflict = true };
        var clock = new FakeClock(FixedNow);
        var proposal = Proposal.Create("CUST-1", "AUTO_BASIC", 1000m, 50m, FixedNow);
        repository.StoredProposals[proposal.Id] = proposal;

        // When reloaded after conflict, simulate that another request set it to Rejected
        var reloaded = Proposal.Create("CUST-1", "AUTO_BASIC", 1000m, 50m, FixedNow);
        reloaded.Reject(FixedNow.AddMinutes(1));
        repository.ReloadReturn = reloaded;

        var useCase = new ChangeProposalStatusUseCase(repository, uow, clock);
        var result = await useCase.ExecuteAsync(proposal.Id.Value, ProposalStatus.Approved, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("proposal_concurrency_conflict", result.Error?.Code);
    }

    [Fact]
    public async Task PA_07_CancellationToken_IsPropagated()
    {
        var repository = new FakeProposalRepository();
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var useCase = new CreateProposalUseCase(repository, uow, clock);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(new CreateProposalCommand("CUST", "PROD", 100m, 10m), cts.Token));
    }

    [Fact]
    public async Task PA_08_ChangeStatus_ConcurrencyConflictWithSameDecision_ReturnsSuccessIdempotent()
    {
        var repository = new FakeProposalRepository();
        var uow = new FakeUnitOfWork { ReturnConflict = true };
        var clock = new FakeClock(FixedNow);
        var proposal = Proposal.Create("CUST-1", "AUTO_BASIC", 1000m, 50m, FixedNow);
        repository.StoredProposals[proposal.Id] = proposal;

        // When reloaded, simulate that another concurrent request already approved it
        var reloaded = Proposal.Create("CUST-1", "AUTO_BASIC", 1000m, 50m, FixedNow);
        reloaded.Approve(FixedNow.AddMinutes(1));
        repository.ReloadReturn = reloaded;

        var useCase = new ChangeProposalStatusUseCase(repository, uow, clock);
        var result = await useCase.ExecuteAsync(proposal.Id.Value, ProposalStatus.Approved, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(ProposalStatus.Approved, result.Value.Status);
    }

    private sealed class FakeProposalRepository : IProposalRepository
    {
        public readonly List<Proposal> AddedProposals = [];
        public readonly Dictionary<ProposalId, Proposal> StoredProposals = [];
        public Proposal? ReloadReturn;

        public Task AddAsync(Proposal proposal, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddedProposals.Add(proposal);
            StoredProposals[proposal.Id] = proposal;
            return Task.CompletedTask;
        }

        public Task<Proposal?> GetByIdAsync(ProposalId id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A fresh read sees whatever a concurrent request has committed meanwhile.
            return Task.FromResult(ReloadReturn ?? StoredProposals.GetValueOrDefault(id));
        }

        public Task<Proposal?> GetForDecisionAsync(ProposalId id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredProposals.TryGetValue(id, out var proposal);
            return Task.FromResult(proposal);
        }

        public Task<Page<Proposal>> ListAsync(ProposalStatus? status, int page, int pageSize, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = StoredProposals.Values.AsEnumerable();
            if (status.HasValue) query = query.Where(p => p.Status == status.Value);
            var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            return Task.FromResult(new Page<Proposal>(items, page, pageSize, StoredProposals.Count));
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }
        public bool ReturnConflict { get; set; }

        public Task<CommitOutcome> SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveChangesCalls++;
            return Task.FromResult(ReturnConflict ? CommitOutcome.ConcurrencyConflict : CommitOutcome.Saved);
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
