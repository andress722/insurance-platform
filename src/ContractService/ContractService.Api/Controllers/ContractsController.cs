using ContractService.Api.Contracts;
using ContractService.Api.ErrorHandling;
using ContractService.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ContractService.Api.Controllers;

[ApiController]
[Route("api/v1/contracts")]
[Produces("application/json")]
public sealed class ContractsController(ICreateContractUseCase create, IGetContractByIdUseCase getById, IGetContractByProposalUseCase getByProposal, ILogger<ContractsController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ContractResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<IActionResult> Create(CreateContractRequest request, CancellationToken cancellationToken)
    {
        var result = await create.ExecuteAsync(request.ProposalId, cancellationToken);
        logger.LogInformation(new EventId(3100, "ContractCreation"), "Contract creation for {ProposalId}: {Outcome}", request.ProposalId, result.Error?.ToString() ?? "Created");
        if (!result.IsSuccess) return ApiProblems.From(result.Error!.Value, HttpContext);

        var response = ContractResponse.From(result.Value!);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ContractResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await getById.ExecuteAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(ContractResponse.From(result.Value!)) : ApiProblems.From(result.Error!.Value, HttpContext);
    }

    [HttpGet("by-proposal/{proposalId}")]
    [ProducesResponseType(typeof(ContractResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetByProposal(Guid proposalId, CancellationToken cancellationToken)
    {
        var result = await getByProposal.ExecuteAsync(proposalId, cancellationToken);
        return result.IsSuccess ? Ok(ContractResponse.From(result.Value!)) : ApiProblems.From(result.Error!.Value, HttpContext);
    }
}
