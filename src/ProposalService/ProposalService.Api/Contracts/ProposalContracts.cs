using System.ComponentModel.DataAnnotations;
using ProposalService.Application.Proposals;
using ProposalService.Domain.Proposals;

namespace ProposalService.Api.Contracts;

public sealed record CreateProposalRequest([Required] string CustomerId, [Required] string ProductCode,
    [Required] decimal? InsuredAmount, [Required] decimal? MonthlyPremium);

public sealed record ChangeStatusRequest([Required] string? Status);

public sealed record ProposalResponse(Guid Id, string CustomerId, string ProductCode, decimal InsuredAmount,
    decimal MonthlyPremium, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int Version)
{
    public static ProposalResponse From(ProposalOutput output) => new(output.Id, output.CustomerId, output.ProductCode,
        output.InsuredAmount, output.MonthlyPremium, ProposalStatusNames.ToWire(output.Status),
        output.CreatedAtUtc, output.UpdatedAtUtc, output.Version);
}

public sealed record ProposalPageResponse(IReadOnlyList<ProposalResponse> Items, int Page, int PageSize, long TotalItems, long TotalPages)
{
    public static ProposalPageResponse From(ProposalPage page) =>
        new([.. page.Items.Select(ProposalResponse.From)], page.Page, page.PageSize, page.TotalItems, page.TotalPages);
}

/// <summary>Owns the wire vocabulary of the v1 contract, so renaming a domain member cannot change the API.</summary>
public static class ProposalStatusNames
{
    public const string UnderReview = "under_review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static string ToWire(ProposalStatus status) => status switch
    {
        ProposalStatus.UnderReview => UnderReview,
        ProposalStatus.Approved => Approved,
        ProposalStatus.Rejected => Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static bool TryParse(string? value, out ProposalStatus status)
    {
        switch (value)
        {
            case UnderReview: status = ProposalStatus.UnderReview; return true;
            case Approved: status = ProposalStatus.Approved; return true;
            case Rejected: status = ProposalStatus.Rejected; return true;
            default: status = default; return false;
        }
    }
}
