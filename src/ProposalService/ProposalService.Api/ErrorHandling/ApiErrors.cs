using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ProposalService.Application.Proposals;

namespace ProposalService.Api.ErrorHandling;

public static class ApiErrors
{
    public static ObjectResult Create(HttpContext context, Error error)
    {
        var (status, title) = error.Code switch
        {
            "validation_failed" => (400, "Request validation failed"),
            "proposal_not_found" => (404, "Proposal was not found"),
            "invalid_status_transition" => (409, "Invalid status transition"),
            "proposal_concurrency_conflict" => (409, "Proposal was decided concurrently"),
            _ => (500, "Unexpected failure")
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = error.Detail,
            Type = "https://errors.insurance.local/" + error.Code.Replace('_', '-'),
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        if (error.Field is not null) problem.Extensions["errors"] = new Dictionary<string, string[]> { [error.Field] = [error.Detail] };
        var result = new ObjectResult(problem) { StatusCode = status };
        result.ContentTypes.Add("application/problem+json");
        return result;
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

public sealed class UnexpectedExceptionHandler(ILogger<UnexpectedExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(new EventId(500, "UnhandledFailure"), "Unexpected request failure. TraceId {TraceId}; ExceptionType {ExceptionType}", context.TraceIdentifier, exception.GetType().Name);
        var result = ApiErrors.Create(context, new Error("internal_error", "An unexpected error occurred."));
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync((ProblemDetails)result.Value!, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
