namespace ContractService.Infrastructure.ProposalApi;

public sealed class ProposalApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Total budget for the eligibility read, retries included.</summary>
    public double TimeoutSeconds { get; set; } = 2;

    /// <summary>Budget of a single attempt, so a slow dependency still leaves room for the retry.</summary>
    public double AttemptTimeoutSeconds { get; set; } = 0.8;

    /// <summary>Additional attempts after the first one: 0 or 1, so a read never costs more than two requests.</summary>
    public int RetryCount { get; set; } = 1;
}
