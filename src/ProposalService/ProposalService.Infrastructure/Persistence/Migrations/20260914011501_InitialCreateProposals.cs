using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProposalService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    insured_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    monthly_premium = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposals", x => x.id);
                    table.CheckConstraint("ck_proposals_insured_amount", "insured_amount > 0");
                    table.CheckConstraint("ck_proposals_monthly_premium", "monthly_premium > 0");
                    table.CheckConstraint("ck_proposals_status", "status IN ('under_review', 'approved', 'rejected')");
                    table.CheckConstraint("ck_proposals_version", "version >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_proposals_status_created_at",
                table: "proposals",
                columns: new[] { "status", "created_at_utc" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proposals");
        }
    }
}
