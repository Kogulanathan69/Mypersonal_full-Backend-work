using System.Text.RegularExpressions;
using BuildMind.Application.Features.PlanEditor.Integration;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Request-scoped record of why the Captain AI proposal port rejected an instruction. The M3 orchestration reports
/// every rejection as <c>AI_PROPOSAL_INVALID</c>; the Captain endpoint uses this to return the specific, stable code
/// (for example <c>AMBIGUOUS_TARGET</c> or <c>MODEL_DECLINED_OR_INVALID</c>) without changing the M3 contract.
/// </summary>
public sealed partial class AiProposalRejectionContext
{
    public string? Code { get; private set; }

    /// <summary>Records a port-defined code; anything that is not an upper-case constant identifier is ignored.</summary>
    public void Record(string? code)
    {
        if (code is not null && CodePattern().IsMatch(code)) Code = code;
    }

    [GeneratedRegex("^[A-Z][A-Z_]{2,63}$")]
    private static partial Regex CodePattern();
}

/// <summary>Decorates the configured Captain proposal port and records rejection codes into the request scope.</summary>
public sealed class RecordingAiEditProposalPort(IAiEditProposalPort inner, AiProposalRejectionContext rejections) : IAiEditProposalPort
{
    public async Task<AiEditProposal> ProposeAsync(Guid projectId, Guid planId, Guid baseRevisionId, string customerInstruction,
        string correlationId, CancellationToken cancellationToken)
    {
        var proposal = await inner.ProposeAsync(projectId, planId, baseRevisionId, customerInstruction, correlationId, cancellationToken)
            .ConfigureAwait(false);
        if (proposal is { Status: AiEditProposalStatus.Rejected, Diagnostics: { Count: > 0 } diagnostics })
            rejections.Record(diagnostics[0]?.Code);
        return proposal;
    }
}
