using ContractService.Domain.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ContractService.Infrastructure.Persistence;

public sealed class ContractDbContext(DbContextOptions<ContractDbContext> options) : DbContext(options)
{
    public DbSet<Contract> Contracts => Set<Contract>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var contract = modelBuilder.Entity<Contract>();
        contract.ToTable("contracts");
        contract.HasKey(x => x.Id).HasName("pk_contracts");
        contract.Property(x => x.Id).HasConversion(id => id.Value, value => new ContractId(value)).HasColumnName("id").ValueGeneratedNever();
        contract.Property(x => x.ProposalId).HasColumnName("proposal_id").IsRequired();
        contract.Property(x => x.ContractedAtUtc).HasColumnName("contracted_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        contract.HasIndex(x => x.ProposalId).IsUnique().HasDatabaseName("ux_contracts_proposal_id");
    }
}
