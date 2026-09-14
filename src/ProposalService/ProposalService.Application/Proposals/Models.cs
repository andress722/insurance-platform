using ProposalService.Domain.Proposals;

namespace ProposalService.Application.Proposals;

public sealed record Error(string Code, string Detail, string? Field = null);
public sealed record Result<T>(T? Value, Error? Error)
{
    public bool IsSuccess => Error is null;
    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(string code, string detail, string? field = null) => new(default, new Error(code, detail, field));
}
public sealed record ProposalOutput(Guid Id, string CustomerId, string ProductCode, decimal InsuredAmount,
    decimal MonthlyPremium, ProposalStatus Status, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int Version)
{
    public static ProposalOutput From(Proposal p) => new(p.Id.Value, p.CustomerId, p.ProductCode, p.InsuredAmount,
        p.MonthlyPremium, p.Status, p.CreatedAtUtc, p.UpdatedAtUtc, p.Version);
}
public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, long TotalItems);
public sealed record ProposalPage(IReadOnlyList<ProposalOutput> Items, int Page, int PageSize, long TotalItems, long TotalPages);
public sealed record CreateProposalCommand(string CustomerId, string ProductCode, decimal InsuredAmount, decimal MonthlyPremium);

public interface ICreateProposalUseCase { Task<Result<ProposalOutput>> ExecuteAsync(CreateProposalCommand command, CancellationToken cancellationToken); }
public interface IGetProposalByIdUseCase { Task<Result<ProposalOutput>> ExecuteAsync(Guid id, CancellationToken cancellationToken); }
public interface IListProposalsUseCase { Task<Result<ProposalPage>> ExecuteAsync(ProposalStatus? status, int page, int pageSize, CancellationToken cancellationToken); }
public interface IChangeProposalStatusUseCase { Task<Result<ProposalOutput>> ExecuteAsync(Guid id, ProposalStatus status, CancellationToken cancellationToken); }
