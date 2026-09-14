namespace ProposalService.Domain.Proposals;

public readonly record struct ProposalId
{
    public Guid Value { get; }
    public ProposalId(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("Proposal ID must not be empty.", nameof(value));
        Value = value;
    }
}

public enum ProposalStatus { UnderReview, Approved, Rejected }

public sealed class ProposalRuleException(string code, string message, string? field = null) : Exception(message)
{
    public string Code { get; } = code;
    public string? Field { get; } = field;
}

public sealed class Proposal
{
    private Proposal() { }
    public ProposalId Id { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public string ProductCode { get; private set; } = string.Empty;
    public decimal InsuredAmount { get; private set; }
    public decimal MonthlyPremium { get; private set; }
    public ProposalStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public int Version { get; private set; }

    public static Proposal Create(string customerId, string productCode, decimal insuredAmount, decimal monthlyPremium, DateTimeOffset now)
    {
        ValidateUtc(now);
        var customer = ValidateText(customerId, 64, "customerId");
        var product = ValidateText(productCode, 50, "productCode").ToUpperInvariant();
        ValidateMoney(insuredAmount, "insuredAmount");
        ValidateMoney(monthlyPremium, "monthlyPremium");
        return new Proposal
        {
            Id = new ProposalId(Guid.NewGuid()),
            CustomerId = customer,
            ProductCode = product,
            InsuredAmount = insuredAmount,
            MonthlyPremium = monthlyPremium,
            Status = ProposalStatus.UnderReview,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1
        };
    }

    public void Approve(DateTimeOffset now) => Decide(ProposalStatus.Approved, now);
    public void Reject(DateTimeOffset now) => Decide(ProposalStatus.Rejected, now);

    private void Decide(ProposalStatus status, DateTimeOffset now)
    {
        ValidateUtc(now);
        if (Status == status) return;
        if (Status != ProposalStatus.UnderReview)
            throw new ProposalRuleException("invalid_status_transition", "A final decision cannot be changed.");
        Status = status;
        UpdatedAtUtc = now;
        Version++;
    }

    private static string ValidateText(string? value, int maximum, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maximum)
            throw new ProposalRuleException("validation_failed", $"The value must contain between 1 and {maximum} characters.", field);
        return normalized;
    }

    private static void ValidateMoney(decimal value, string field)
    {
        if (value <= 0 || value > 9999999999999999.99m || decimal.Round(value, 2) != value)
            throw new ProposalRuleException("validation_failed", "The value must be positive, fit numeric(18,2), and have at most two decimal places.", field);
    }

    private static void ValidateUtc(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero) throw new ArgumentException("The timestamp must be UTC.", nameof(now));
    }
}
