namespace BuildMind.Application.Abstractions.Regulations;

/// <summary>
/// M7 regulation evidence port (evidence contract v1, lead-approved 2026-11-19). Returns cited source passages and an
/// explicit evidence status. It never returns a generated answer, a legal-currency claim or a compliance decision.
/// Failures surface as <see cref="Errors.DependencyFailureException"/> (503/504), never as fabricated evidence.
/// </summary>
public interface IRegulationEvidenceSource
{
    Task<RegulationEvidence> SearchAsync(RegulationSearchQuery query, CancellationToken cancellationToken);
}

/// <param name="Query">1–500 characters.</param>
/// <param name="Jurisdiction">Exact approved jurisdiction label (no alias or locality inference), or null.</param>
/// <param name="DocumentIds">1–3 approved document identities (64 lowercase hex), or null.</param>
/// <param name="MaxPassages">1–10.</param>
public sealed record RegulationSearchQuery(
    string Query,
    string? Jurisdiction = null,
    IReadOnlyList<string>? DocumentIds = null,
    int MaxPassages = 5);

public enum RegulationEvidenceStatus
{
    EvidenceFound,
    NoMatchingEvidence,
    InsufficientJurisdictionEvidence,
    InsufficientSourceEvidence,
    AmbiguousEvidence,
}

public enum PassageEvidenceStatus
{
    EvidenceCandidate,
    AmbiguousEvidence,
}

/// <summary>Always <c>LegalStatusVerified = false</c> and <c>AnswerGenerated = false</c> (enforced on receipt).</summary>
public sealed record RegulationEvidence(
    RegulationEvidenceStatus Status,
    IReadOnlyList<RegulationPassage> Passages,
    IReadOnlyList<string> Warnings);

/// <param name="Page">One-based PDF page position (not the printed page label).</param>
/// <param name="StartChar">Unicode code-point offset within the extracted page text.</param>
/// <param name="Score">Lexical relevance only, never legal confidence.</param>
public sealed record RegulationPassage(
    string PassageId,
    string DocumentId,
    string SourceTitle,
    string? Jurisdiction,
    int Page,
    int StartChar,
    int EndChar,
    string Excerpt,
    double Score,
    PassageEvidenceStatus EvidenceStatus,
    IReadOnlyList<string> Warnings);
