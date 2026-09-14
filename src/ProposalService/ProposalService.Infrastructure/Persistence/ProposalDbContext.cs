using Microsoft.EntityFrameworkCore;
using ProposalService.Domain.Proposals;

namespace ProposalService.Infrastructure.Persistence;

public sealed class ProposalDbContext(DbContextOptions<ProposalDbContext> options) : DbContext(options)
{
    public DbSet<Proposal> Proposals => Set<Proposal>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Proposal>();
        entity.ToTable("proposals", table =>
        {
            table.HasCheckConstraint("ck_proposals_insured_amount", "insured_amount > 0");
            table.HasCheckConstraint("ck_proposals_monthly_premium", "monthly_premium > 0");
            table.HasCheckConstraint("ck_proposals_status", "status IN ('under_review', 'approved', 'rejected')");
            table.HasCheckConstraint("ck_proposals_version", "version >= 1");
        });
        entity.HasKey(p => p.Id).HasName("pk_proposals");
        entity.Property(p => p.Id).HasColumnName("id").HasConversion(id => id.Value, value => new ProposalId(value)).ValueGeneratedNever();
        entity.Property(p => p.CustomerId).HasColumnName("customer_id").HasMaxLength(64).IsRequired();
        entity.Property(p => p.ProductCode).HasColumnName("product_code").HasMaxLength(50).IsRequired();
        entity.Property(p => p.InsuredAmount).HasColumnName("insured_amount").HasPrecision(18, 2);
        entity.Property(p => p.MonthlyPremium).HasColumnName("monthly_premium").HasPrecision(18, 2);
        entity.Property(p => p.Status).HasColumnName("status").HasMaxLength(24).HasConversion(
            status => status == ProposalStatus.UnderReview ? "under_review" : status == ProposalStatus.Approved ? "approved" : "rejected",
            status => status == "under_review" ? ProposalStatus.UnderReview : status == "approved" ? ProposalStatus.Approved : ProposalStatus.Rejected);
        entity.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc");
        entity.Property(p => p.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.Property(p => p.Version).HasColumnName("version").IsConcurrencyToken();
        entity.HasIndex(p => new { p.Status, p.CreatedAtUtc }).IsDescending(false, true).HasDatabaseName("ix_proposals_status_created_at");
    }
}
