using ProposalService.Domain.Proposals;

namespace ProposalService.UnitTests;

public sealed class ProposalDomainTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PU_01_Create_WithValidData_ReturnsProposalInUnderReviewStatus()
    {
        var proposal = Proposal.Create("CUST-1", "AUTO_BASIC", 50000.00m, 150.00m, NowUtc);

        Assert.NotEqual(Guid.Empty, proposal.Id.Value);
        Assert.Equal("CUST-1", proposal.CustomerId);
        Assert.Equal("AUTO_BASIC", proposal.ProductCode);
        Assert.Equal(50000.00m, proposal.InsuredAmount);
        Assert.Equal(150.00m, proposal.MonthlyPremium);
        Assert.Equal(ProposalStatus.UnderReview, proposal.Status);
        Assert.Equal(NowUtc, proposal.CreatedAtUtc);
        Assert.Equal(NowUtc, proposal.UpdatedAtUtc);
        Assert.Equal(1, proposal.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a_very_long_customer_id_that_exceeds_sixty_four_characters_limit_1234567890")]
    public void PU_02_Create_WithInvalidCustomerId_ThrowsValidationFailed(string? customerId)
    {
        var ex = Assert.Throws<ProposalRuleException>(() =>
            Proposal.Create(customerId!, "AUTO_BASIC", 50000m, 150m, NowUtc));

        Assert.Equal("validation_failed", ex.Code);
        Assert.Equal("customerId", ex.Field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a_very_long_product_code_that_exceeds_fifty_characters_limit_1234567890")]
    public void PU_03_Create_WithInvalidProductCode_ThrowsValidationFailed(string? productCode)
    {
        var ex = Assert.Throws<ProposalRuleException>(() =>
            Proposal.Create("CUST-1", productCode!, 50000m, 150m, NowUtc));

        Assert.Equal("validation_failed", ex.Code);
        Assert.Equal("productCode", ex.Field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100.50)]
    public void PU_04_Create_WithZeroOrNegativeInsuredAmount_ThrowsValidationFailed(decimal amount)
    {
        var ex = Assert.Throws<ProposalRuleException>(() =>
            Proposal.Create("CUST-1", "AUTO_BASIC", amount, 150m, NowUtc));

        Assert.Equal("validation_failed", ex.Code);
        Assert.Equal("insuredAmount", ex.Field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-50)]
    public void PU_04_Create_WithZeroOrNegativeMonthlyPremium_ThrowsValidationFailed(decimal premium)
    {
        var ex = Assert.Throws<ProposalRuleException>(() =>
            Proposal.Create("CUST-1", "AUTO_BASIC", 50000m, premium, NowUtc));

        Assert.Equal("validation_failed", ex.Code);
        Assert.Equal("monthlyPremium", ex.Field);
    }

    [Fact]
    public void PU_05_Create_WithMoreThanTwoDecimalPlaces_ThrowsValidationFailed()
    {
        var ex1 = Assert.Throws<ProposalRuleException>(() =>
            Proposal.Create("CUST-1", "AUTO_BASIC", 50000.005m, 150m, NowUtc));
        Assert.Equal("validation_failed", ex1.Code);
        Assert.Equal("insuredAmount", ex1.Field);

        var ex2 = Assert.Throws<ProposalRuleException>(() =>
            Proposal.Create("CUST-1", "AUTO_BASIC", 50000m, 150.999m, NowUtc));
        Assert.Equal("validation_failed", ex2.Code);
        Assert.Equal("monthlyPremium", ex2.Field);
    }

    [Fact]
    public void PU_06_Approve_WhenUnderReview_UpdatesStatusTimestampAndIncrementsVersion()
    {
        var proposal = Proposal.Create("CUST-1", "AUTO_BASIC", 50000m, 150m, NowUtc);
        var decisionTime = NowUtc.AddMinutes(5);

        proposal.Approve(decisionTime);

        Assert.Equal(ProposalStatus.Approved, proposal.Status);
        Assert.Equal(decisionTime, proposal.UpdatedAtUtc);
        Assert.Equal(2, proposal.Version);
    }

    [Fact]
    public void PU_07_Reject_WhenUnderReview_UpdatesStatusTimestampAndIncrementsVersion()
    {
        var proposal = Proposal.Create("CUST-1", "AUTO_BASIC", 50000m, 150m, NowUtc);
        var decisionTime = NowUtc.AddMinutes(10);

        proposal.Reject(decisionTime);

        Assert.Equal(ProposalStatus.Rejected, proposal.Status);
        Assert.Equal(decisionTime, proposal.UpdatedAtUtc);
        Assert.Equal(2, proposal.Version);
    }

    [Fact]
    public void PU_08_Approve_WhenAlreadyApproved_IsIdempotentAndDoesNotChangeVersionOrDate()
    {
        var proposal = Proposal.Create("CUST-1", "AUTO_BASIC", 50000m, 150m, NowUtc);
        var firstDecisionTime = NowUtc.AddMinutes(5);
        proposal.Approve(firstDecisionTime);

        var secondDecisionTime = NowUtc.AddMinutes(15);
        proposal.Approve(secondDecisionTime);

        Assert.Equal(ProposalStatus.Approved, proposal.Status);
        Assert.Equal(firstDecisionTime, proposal.UpdatedAtUtc);
        Assert.Equal(2, proposal.Version);
    }

    [Fact]
    public void PU_08_Reject_WhenAlreadyRejected_IsIdempotentAndDoesNotChangeVersionOrDate()
    {
        var proposal = Proposal.Create("CUST-1", "AUTO_BASIC", 50000m, 150m, NowUtc);
        var firstDecisionTime = NowUtc.AddMinutes(5);
        proposal.Reject(firstDecisionTime);

        var secondDecisionTime = NowUtc.AddMinutes(15);
        proposal.Reject(secondDecisionTime);

        Assert.Equal(ProposalStatus.Rejected, proposal.Status);
        Assert.Equal(firstDecisionTime, proposal.UpdatedAtUtc);
        Assert.Equal(2, proposal.Version);
    }

    [Fact]
    public void PU_09_Reject_WhenAlreadyApproved_ThrowsInvalidStatusTransition()
    {
        var proposal = Proposal.Create("CUST-1", "AUTO_BASIC", 50000m, 150m, NowUtc);
        proposal.Approve(NowUtc.AddMinutes(5));

        var ex = Assert.Throws<ProposalRuleException>(() => proposal.Reject(NowUtc.AddMinutes(10)));
        Assert.Equal("invalid_status_transition", ex.Code);
    }

    [Fact]
    public void PU_10_Approve_WhenAlreadyRejected_ThrowsInvalidStatusTransition()
    {
        var proposal = Proposal.Create("CUST-1", "AUTO_BASIC", 50000m, 150m, NowUtc);
        proposal.Reject(NowUtc.AddMinutes(5));

        var ex = Assert.Throws<ProposalRuleException>(() => proposal.Approve(NowUtc.AddMinutes(10)));
        Assert.Equal("invalid_status_transition", ex.Code);
    }

    [Fact]
    public void Normalization_TrimsCustomerId_AndUppercaseProductCode()
    {
        var proposal = Proposal.Create("   CUST-XYZ   ", "  auto_basic  ", 1000m, 50m, NowUtc);

        Assert.Equal("CUST-XYZ", proposal.CustomerId);
        Assert.Equal("AUTO_BASIC", proposal.ProductCode);
    }

    [Fact]
    public void Create_WithNonUtcTimestamp_ThrowsArgumentException()
    {
        var localTime = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.FromHours(-3));

        Assert.Throws<ArgumentException>(() =>
            Proposal.Create("CUST-1", "AUTO_BASIC", 1000m, 50m, localTime));
    }

    [Fact]
    public void ProposalId_WithEmptyGuid_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ProposalId(Guid.Empty));
    }
}
