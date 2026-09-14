using ContractService.Domain.Contracts;

namespace ContractService.Application.Contracts;

public enum ContractError
{
    ValidationFailed, ContractNotFound, ContractAlreadyExists, ProposalNotFound,
    ProposalNotApproved, ProposalServiceUnavailable, ProposalServiceInvalidResponse
}

public sealed record Result<T>
{
    private Result(T? value, ContractError? error) { Value = value; Error = error; }
    public T? Value { get; }
    public ContractError? Error { get; }
    public bool IsSuccess => Error is null;
    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(ContractError error) => new(default, error);
}

public sealed record ContractOutput(Guid Id, Guid ProposalId, DateTimeOffset ContractedAtUtc)
{
    public static ContractOutput From(Contract contract) => new(contract.Id.Value, contract.ProposalId, contract.ContractedAtUtc);
}

public interface ICreateContractUseCase
{
    Task<Result<ContractOutput>> ExecuteAsync(Guid proposalId, CancellationToken cancellationToken);
}

public interface IGetContractByIdUseCase
{
    Task<Result<ContractOutput>> ExecuteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IGetContractByProposalUseCase
{
    Task<Result<ContractOutput>> ExecuteAsync(Guid proposalId, CancellationToken cancellationToken);
}
