using Microsoft.AspNetCore.Mvc;
using ProposalService.Api.Contracts;
using ProposalService.Api.ErrorHandling;
using ProposalService.Application.Proposals;

namespace ProposalService.Api.Controllers;

[ApiController]
[Route("api/v1/proposals")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(500)]
public sealed class ProposalsController(ICreateProposalUseCase create, IGetProposalByIdUseCase get,
    IListProposalsUseCase list, IChangeProposalStatusUseCase change, ILogger<ProposalsController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ProposalResponse>(201)]
    public async Task<IActionResult> Create(CreateProposalRequest request, CancellationToken cancellationToken)
    {
        var result = await create.ExecuteAsync(new(request.CustomerId, request.ProductCode, request.InsuredAmount!.Value, request.MonthlyPremium!.Value), cancellationToken);
        if (!result.IsSuccess) return ApiErrors.Create(HttpContext, result.Error!);
        logger.LogInformation(new EventId(100, "ProposalCreated"), "Proposal {ProposalId} created. Outcome {Outcome}", result.Value!.Id, "created");
        return Created($"/api/v1/proposals/{result.Value.Id}", ProposalResponse.From(result.Value));
    }

    [HttpGet("{id}")]
    [ProducesResponseType<ProposalResponse>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await get.ExecuteAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(ProposalResponse.From(result.Value!)) : ApiErrors.Create(HttpContext, result.Error!);
    }

    [HttpGet]
    [ProducesResponseType<ProposalPageResponse>(200)]
    public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (status is not null && !ProposalStatusNames.TryParse(status, out _))
            return ApiErrors.Create(HttpContext, new Error("validation_failed", "The status is invalid.", "status"));

        ProposalStatusNames.TryParse(status, out var parsed);
        var result = await list.ExecuteAsync(status is null ? null : parsed, page, pageSize, cancellationToken);
        return result.IsSuccess ? Ok(ProposalPageResponse.From(result.Value!)) : ApiErrors.Create(HttpContext, result.Error!);
    }

    [HttpPatch("{id}/status")]
    [ProducesResponseType<ProposalResponse>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<IActionResult> Change(Guid id, ChangeStatusRequest request, CancellationToken cancellationToken)
    {
        if (!ProposalStatusNames.TryParse(request.Status, out var status))
            return ApiErrors.Create(HttpContext, new Error("validation_failed", "The status is invalid.", "status"));

        var result = await change.ExecuteAsync(id, status, cancellationToken);
        logger.LogInformation(new EventId(101, "ProposalDecision"), "Proposal {ProposalId} decision. Outcome {Outcome}", id, result.Error?.Code ?? "success");
        return result.IsSuccess ? Ok(ProposalResponse.From(result.Value!)) : ApiErrors.Create(HttpContext, result.Error!);
    }
}
