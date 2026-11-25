using System.Text;
using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Application.Features.Reports.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Decorates the M5 preliminary renderer (unchanged) with a "Regulation evidence consulted" appendix built from REAL
/// retrieval against the approved, hash-verified corpus. Every citation shows the source title, the full document
/// SHA-256 (= approved identity pin), the 1-based PDF page, character offsets, passage id and the evidence status.
/// Nothing is generated or summarised: excerpts are quoted source text. When retrieval is unavailable the appendix says
/// so and contains no regulatory content (fail closed for evidence; the preliminary report itself stays valid).
/// The appendix is inside the stored artifact, so the report's recorded SHA-256 protects the citations as well.
/// </summary>
public sealed class EvidenceCitingReportRenderer(IPreliminaryReportRenderer inner, IServiceScopeFactory scopes,
    ILogger<EvidenceCitingReportRenderer> logger) : IPreliminaryReportRenderer
{
    /// <summary>Fixed, documented evidence topics for a residential 2D plan (retrieval queries, not legal questions).</summary>
    public static readonly IReadOnlyList<string> Topics =
    [
        "rear space side space residential",
        "maximum plot coverage",
        "building line road reservation",
        "maximum number of floors permissible height",
    ];

    public const int MaxCitations = 8;

    public async Task<byte[]> RenderAsync(ReportRenderData data, CancellationToken cancellationToken)
    {
        var pdf = await inner.RenderAsync(data, cancellationToken).ConfigureAwait(false);
        var (citations, unavailable) = await RetrieveAsync(cancellationToken).ConfigureAwait(false);
        return AppendEvidence(pdf, citations, unavailable);
    }

    private async Task<(IReadOnlyList<RegulationPassage> Citations, bool Unavailable)> RetrieveAsync(CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var source = scope.ServiceProvider.GetService<IRegulationEvidenceSource>();
        if (source is null) return ([], true);
        var found = new List<RegulationPassage>();
        try
        {
            foreach (var topic in Topics)
            {
                var evidence = await source.SearchAsync(new RegulationSearchQuery(topic, MaxPassages: 2), token).ConfigureAwait(false);
                if (evidence.Status is RegulationEvidenceStatus.EvidenceFound or RegulationEvidenceStatus.AmbiguousEvidence)
                    found.AddRange(evidence.Passages.Where(p => found.All(f => f.PassageId != p.PassageId)));
            }
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            logger.LogWarning("Report regulation evidence unavailable ({Type})", e.GetType().Name);
            return ([], true);
        }
        return (found.Take(MaxCitations).ToList(), false);
    }

    /// <summary>Appends the evidence page(s) to an already rendered PDF.</summary>
    public static byte[] AppendEvidence(byte[] pdf, IReadOnlyList<RegulationPassage> citations, bool unavailable)
    {
        using var input = new MemoryStream(pdf, writable: false);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var title = new XFont("BuildMind Noto", 11);
        var font = new XFont("BuildMind Noto", 8);
        XGraphics? graphics = null;
        double y = 0;
        void NewPage()
        {
            graphics?.Dispose();
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            y = 50;
        }
        void Write(string text, XFont f)
        {
            foreach (var line in Wrap(Ascii(text), 110))
            {
                if (graphics is null || y > 790) NewPage();
                graphics!.DrawString(line, f, XBrushes.Black, 40, y);
                y += f.Size + 4;
            }
        }
        NewPage();
        Write("Regulation evidence consulted (retrieval only)", title);
        y += 4;
        Write("Quoted passages from the approved regulation corpus, retrieved for fixed residential topics. They are source "
              + "evidence only: not a legal, compliance or currency determination, not filtered to your parcel's authority, and "
              + "not used to set any value in this plan. Legal status of the sources: not verified.", font);
        y += 6;
        if (unavailable)
            Write("Regulation evidence could not be retrieved when this report was generated. No regulatory content is included.", font);
        else if (citations.Count == 0)
            Write("No qualifying evidence was found for the fixed topics. No regulatory content is included.", font);
        var n = 0;
        foreach (var c in citations)
        {
            n++;
            y += 4;
            Write($"[{n}] {c.SourceTitle} | page {c.Page} (PDF page position) | chars {c.StartChar}-{c.EndChar} | {c.EvidenceStatus}", font);
            Write($"     document SHA-256: {c.DocumentId}", font);
            Write($"     passage: {c.PassageId} | jurisdiction label: {c.Jurisdiction ?? "unspecified"}", font);
            var excerpt = c.Excerpt.Length > 600 ? c.Excerpt[..600] + " [...]" : c.Excerpt;
            Write("     \"" + excerpt.Replace('\n', ' ') + "\"", font);
        }
        graphics?.Dispose();
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static string Ascii(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(ch switch
            {
                '–' or '—' or '−' or '⸺' => '-',
                '‘' or '’' => '\'',
                '“' or '”' => '"',
                _ when ch is >= ' ' and <= '~' => ch,
                _ when char.IsWhiteSpace(ch) => ' ',
                _ => '?',
            });
        return builder.ToString();
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            var w = word;
            while (w.Length > width) { if (line.Length > 0) { yield return line.ToString(); line.Clear(); } yield return w[..width]; w = w[width..]; }
            if (line.Length + w.Length + 1 > width && line.Length > 0) { yield return line.ToString(); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(w);
        }
        if (line.Length > 0) yield return line.ToString();
    }
}
