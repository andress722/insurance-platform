namespace ContractService.Domain.Contracts;

public readonly record struct ContractId
{
    public ContractId(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("Contract ID must not be empty.", nameof(value));
        Value = value;
    }

    public Guid Value { get; }
}
