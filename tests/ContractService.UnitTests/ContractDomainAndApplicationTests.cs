using ContractService.Application.Abstractions;
using ContractService.Application.Contracts;
using ContractService.Domain.Contracts;

namespace ContractService.UnitTests;

public sealed class ContractDomainAndApplicationTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CU_01_CreateContract_ValidData_SetsProperties()
    {
        var proposalId = Guid.NewGuid();
        var contract = Contract.Create(proposalId, FixedNow);

        Assert.NotEqual(Guid.Empty, contract.Id.Value);
        Assert.Equal(proposalId, contract.ProposalId);
        Assert.Equal(FixedNow, contract.ContractedAtUtc);
    }

    [Fact]
    public void CU_02_CreateContract_EmptyProposalId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Contract.Create(Guid.Empty, FixedNow));
    }

    [Fact]
    public void CU_02_CreateContract_NonUtcTimestamp_ThrowsArgumentException()
    {
        var localTime = new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.FromHours(2));
        Assert.Throws<ArgumentException>(() => Contract.Create(Guid.NewGuid(), localTime));
    }

    [Fact]
    public void ContractId_EmptyGuid_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ContractId(Guid.Empty));
    }

    [Fact]
    public async Task CA_01_CreateContract_WhenContractAlreadyExistsLocally_DoesNotCallGateway()
    {
        var repository = new FakeContractRepository();
        var gateway = new FakeProposalGateway(ProposalEligibilityResult.Approved);
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);
        var proposalId = Guid.NewGuid();

        var existing = Contract.Create(proposalId, FixedNow);
        repository.StoredContractsByProposal[proposalId] = existing;

        var useCase = new CreateContract(repository, gateway, uow, clock);
        var result = await useCase.ExecuteAsync(proposalId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContractError.ContractAlreadyExists, result.Error);
        Assert.Equal(0, gateway.CallCount);
        Assert.Equal(0, uow.SaveChangesCalls);
    }

    [Fact]
    public async Task CA_02_CreateContract_WhenProposalNotFoundRemotely_ReturnsProposalNotFound_AndDoesNotPersist()
    {
        var repository = new FakeContractRepository();
        var gateway = new FakeProposalGateway(ProposalEligibilityResult.NotFound);
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);

        var useCase = new CreateContract(repository, gateway, uow, clock);
        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContractError.ProposalNotFound, result.Error);
        Assert.Empty(repository.AddedContracts);
        Assert.Equal(0, uow.SaveChangesCalls);
    }

    [Theory]
    [InlineData(ProposalEligibilityResult.UnderReview)]
    [InlineData(ProposalEligibilityResult.Rejected)]
    public async Task CA_03_CA_04_CreateContract_WhenProposalNotApproved_ReturnsProposalNotApproved_AndDoesNotPersist(ProposalEligibilityResult eligibility)
    {
        var repository = new FakeContractRepository();
        var gateway = new FakeProposalGateway(eligibility);
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);

        var useCase = new CreateContract(repository, gateway, uow, clock);
        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContractError.ProposalNotApproved, result.Error);
        Assert.Empty(repository.AddedContracts);
        Assert.Equal(0, uow.SaveChangesCalls);
    }

    [Fact]
    public async Task CA_05_CreateContract_WhenProposalApproved_CreatesAndPersistsContract()
    {
        var repository = new FakeContractRepository();
        var gateway = new FakeProposalGateway(ProposalEligibilityResult.Approved);
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);
        var proposalId = Guid.NewGuid();

        var useCase = new CreateContract(repository, gateway, uow, clock);
        var result = await useCase.ExecuteAsync(proposalId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(proposalId, result.Value.ProposalId);
        Assert.Equal(FixedNow, result.Value.ContractedAtUtc);
        Assert.Single(repository.AddedContracts);
        Assert.Equal(1, uow.SaveChangesCalls);
    }

    [Fact]
    public async Task CA_06_CreateContract_WhenGatewayUnavailable_ReturnsProposalServiceUnavailable()
    {
        var repository = new FakeContractRepository();
        var gateway = new FakeProposalGateway(ProposalEligibilityResult.Unavailable);
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);

        var useCase = new CreateContract(repository, gateway, uow, clock);
        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContractError.ProposalServiceUnavailable, result.Error);
        Assert.Empty(repository.AddedContracts);
        Assert.Equal(0, uow.SaveChangesCalls);
    }

    [Fact]
    public async Task CA_07_CreateContract_WhenGatewayReturnsInvalidResponse_ReturnsInvalidResponse()
    {
        var repository = new FakeContractRepository();
        var gateway = new FakeProposalGateway(ProposalEligibilityResult.InvalidResponse);
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);

        var useCase = new CreateContract(repository, gateway, uow, clock);
        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContractError.ProposalServiceInvalidResponse, result.Error);
        Assert.Empty(repository.AddedContracts);
        Assert.Equal(0, uow.SaveChangesCalls);
    }

    [Fact]
    public async Task CA_08_CreateContract_WhenUniqueIndexViolatedOnCommit_ReturnsContractAlreadyExists()
    {
        var repository = new FakeContractRepository();
        var gateway = new FakeProposalGateway(ProposalEligibilityResult.Approved);
        var uow = new FakeUnitOfWork { ReturnOutcome = CommitOutcome.ContractAlreadyExists };
        var clock = new FakeClock(FixedNow);

        var useCase = new CreateContract(repository, gateway, uow, clock);
        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContractError.ContractAlreadyExists, result.Error);
    }

    [Fact]
    public async Task CA_09_CreateContract_PropagatesCancellationToken()
    {
        var repository = new FakeContractRepository();
        var gateway = new FakeProposalGateway(ProposalEligibilityResult.Approved);
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FixedNow);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var useCase = new CreateContract(repository, gateway, uow, clock);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(Guid.NewGuid(), cts.Token));
    }

    [Fact]
    public async Task GetContractById_WhenMissing_ReturnsContractNotFound()
    {
        var repository = new FakeContractRepository();
        var useCase = new GetContractById(repository);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContractError.ContractNotFound, result.Error);
    }

    [Fact]
    public async Task GetContractByProposal_WhenMissing_ReturnsContractNotFound()
    {
        var repository = new FakeContractRepository();
        var useCase = new GetContractByProposal(repository);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContractError.ContractNotFound, result.Error);
    }

    private sealed class FakeContractRepository : IContractRepository
    {
        public readonly List<Contract> AddedContracts = [];
        public readonly Dictionary<ContractId, Contract> StoredContractsById = [];
        public readonly Dictionary<Guid, Contract> StoredContractsByProposal = [];

        public Task AddAsync(Contract contract, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddedContracts.Add(contract);
            StoredContractsById[contract.Id] = contract;
            StoredContractsByProposal[contract.ProposalId] = contract;
            return Task.CompletedTask;
        }

        public Task<Contract?> GetByIdAsync(ContractId id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredContractsById.TryGetValue(id, out var contract);
            return Task.FromResult(contract);
        }

        public Task<Contract?> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredContractsByProposal.TryGetValue(proposalId, out var contract);
            return Task.FromResult(contract);
        }
    }

    private sealed class FakeProposalGateway(ProposalEligibilityResult outcome) : IProposalGateway
    {
        public int CallCount { get; private set; }

        public Task<ProposalEligibilityResult> GetEligibilityAsync(Guid proposalId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(outcome);
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }
        public CommitOutcome ReturnOutcome { get; set; } = CommitOutcome.Saved;

        public Task<CommitOutcome> SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveChangesCalls++;
            return Task.FromResult(ReturnOutcome);
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
