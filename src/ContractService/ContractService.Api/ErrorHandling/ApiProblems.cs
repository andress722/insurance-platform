using System.Diagnostics;
using ContractService.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ContractService.Api.ErrorHandling;

public static class ApiProblems
{
    public static ObjectResult From(ContractError error, HttpContext context)
    {
        var (status, code, title) = error switch
        {
            ContractError.ValidationFailed => (400, "validation_failed", "Request validation failed"),
            ContractError.ContractNotFound => (404, "contract_not_found", "Contract was not found"),
            ContractError.ContractAlreadyExists => (409, "contract_already_exists", "Contract already exists"),
            ContractError.ProposalNotFound => (404, "proposal_not_found", "Proposal was not found"),
            ContractError.ProposalNotApproved => (409, "proposal_not_approved", "Proposal is not approved"),
            ContractError.ProposalServiceUnavailable => (503, "proposal_service_unavailable", "Proposal service is unavailable"),
            ContractError.ProposalServiceInvalidResponse => (502, "proposal_service_invalid_response", "Proposal service returned an invalid response"),
            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = error == ContractError.ProposalNotApproved ? "Only approved proposals can be contracted." : title + ".",
            Type = $"https://errors.insurance.local/{code.Replace('_', '-')}",
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    /// <summary>Stable code for problems produced by the pipeline itself, which carry no application error.</summary>
    public static string CodeForStatus(int status) => status switch
    {
        400 => "bad_request",
        404 => "resource_not_found",
        405 => "method_not_allowed",
        406 => "not_acceptable",
        415 => "unsupported_media_type",
        >= 500 => "internal_error",
        _ => "http_error"
    };
}
