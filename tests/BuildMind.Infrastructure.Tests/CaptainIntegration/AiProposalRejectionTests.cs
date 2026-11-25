using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Infrastructure.CaptainIntegration;

namespace BuildMind.Infrastructure.Tests.CaptainIntegration;

/// <summary>The Captain decorator records the port's specific rejection code for the endpoint (M3 contract unchanged).</summary>
public sealed class AiProposalRejectionTests
{
    private sealed class FixedPort(AiEditProposal proposal) : IAiEditProposalPort
    {
        public Task<AiEditProposal> ProposeAsync(Guid projectId, Guid planId, Guid baseRevisionId, string customerInstruction,
            string correlationId, CancellationToken cancellationToken) => Task.FromResult(proposal);
    }

    private static async Task<(AiEditProposal Result, AiProposalRejectionContext Context)> Run(AiEditProposal proposal)
    {
        var context = new AiProposalRejectionContext();
        var result = await new RecordingAiEditProposalPort(new FixedPort(proposal), context)
            .ProposeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "x", "c", CancellationToken.None);
        return (result, context);
    }

    private static AiEditProposal Rejected(string code) =>
        new(AiEditProposalStatus.Rejected, [], [new PlanEditDiagnosticDto { Code = code, Severity = "ERROR", Message = "m" }]);

    [Fact]
    public async Task Rejection_code_is_recorded_and_the_proposal_is_returned_unchanged()
    {
        var proposal = Rejected("AMBIGUOUS_TARGET");
        var (result, context) = await Run(proposal);
        Assert.Same(proposal, result);
        Assert.Equal("AMBIGUOUS_TARGET", context.Code);
    }

    [Theory]
    [InlineData("lower_case")]
    [InlineData("HAS SPACE")]
    [InlineData("X")]
    [InlineData("<script>")]
    public async Task Non_constant_codes_are_ignored(string code) => Assert.Null((await Run(Rejected(code))).Context.Code);

    [Fact]
    public async Task Proposed_and_unavailable_record_nothing()
    {
        Assert.Null((await Run(new AiEditProposal(AiEditProposalStatus.Proposed, [], []))).Context.Code);
        Assert.Null((await Run(new AiEditProposal(AiEditProposalStatus.Unavailable, [], []))).Context.Code);
        Assert.Null((await Run(new AiEditProposal(AiEditProposalStatus.Rejected, [], []))).Context.Code);
    }
}
