namespace ContractService.Domain.Contracts;

public sealed class Contract
{
    private Contract() { }

    public ContractId Id { get; private set; }
    public Guid ProposalId { get; private set; }
    public DateTimeOffset ContractedAtUtc { get; private set; }

    public static Contract Create(Guid proposalId, DateTimeOffset now)
    {
        if (proposalId == Guid.Empty) throw new ArgumentException("Proposal ID must not be empty.", nameof(proposalId));
        if (now.Offset != TimeSpan.Zero) throw new ArgumentException("Contract time must be UTC.", nameof(now));
        return new Contract { Id = new ContractId(Guid.NewGuid()), ProposalId = proposalId, ContractedAtUtc = now };
    }
}
