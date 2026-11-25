using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BuildMind.Application.Features.Administration;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records.Administration;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Persistent, versioned requirement catalogue (DRAFT → PUBLISHED → RETIRED). Documents are validated with the same
/// strict parser as the file provider. The served catalogue is the PUBLISHED version with the latest effective date not
/// in the future; requirement revisions keep the exact catalogue version they were saved with (historical lookup).
/// Values are product preferences, never building regulations; every version carries a provenance label.
/// </summary>
internal sealed partial class RequirementCatalogueStore(BuildMindDbContext context, TransactionRunner transactions, TimeProvider time)
    : IRequirementCatalogueAdministration
{
    public const int MaxDocumentBytes = 256 * 1024;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    internal static partial Regex VersionPattern();

    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Null when valid; otherwise a stable code. The document's own version must equal the key.</summary>
    public static string? Validate(string version, string documentJson)
    {
        if (!VersionPattern().IsMatch(version)) return "CATALOGUE_VERSION_INVALID";
        if (Encoding.UTF8.GetByteCount(documentJson) > MaxDocumentBytes) return "CATALOGUE_TOO_LARGE";
        try
        {
            var document = JsonSerializer.Deserialize<ConfiguredRequirementCatalogueProvider.CatalogueDocument>(documentJson, Strict);
            if (document is null) return "CATALOGUE_INVALID";
            var catalogue = document.ToCatalogue();
            if (catalogue.Version != version) return "CATALOGUE_VERSION_MISMATCH";
            if (catalogue.Rooms.Count == 0) return "CATALOGUE_INVALID";
            return null;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException)
        {
            return "CATALOGUE_INVALID";
        }
    }

    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static CatalogueVersionView View(RequirementCatalogueVersionRecord r) => new(r.Version, r.Status, r.Provenance, r.EffectiveFromUtc,
        r.RowVersion, r.CreatedBy, r.CreatedAtUtc, r.PublishedBy, r.PublishedAtUtc, r.RetiredAtUtc);

    public async Task<IReadOnlyList<CatalogueVersionView>> ListAsync(CancellationToken token) =>
        (await context.Set<RequirementCatalogueVersionRecord>().AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).Take(200)
            .ToListAsync(token).ConfigureAwait(false)).Select(View).ToArray();

    public async Task<(CatalogueVersionView View, string Document)?> GetAsync(string version, CancellationToken token) =>
        await context.Set<RequirementCatalogueVersionRecord>().AsNoTracking().SingleOrDefaultAsync(r => r.Version == version, token)
            .ConfigureAwait(false) is { } r ? (View(r), r.DocumentJson) : null;

    public async Task<string?> GetServedDocumentAsync(CancellationToken token)
    {
        var now = time.GetUtcNow();
        return await context.Set<RequirementCatalogueVersionRecord>().AsNoTracking()
            .Where(r => r.Status == "PUBLISHED" && r.EffectiveFromUtc <= now)
            .OrderByDescending(r => r.EffectiveFromUtc).ThenByDescending(r => r.PublishedAtUtc)
            .Select(r => r.DocumentJson).FirstOrDefaultAsync(token).ConfigureAwait(false);
    }

    /// <summary>Creates a DRAFT (admin import, or an approved staff proposal inside the approval transaction).</summary>
    internal async Task<AdminResult<CatalogueVersionView>> AddDraftAsync(Guid actor, string version, string provenance, string documentJson,
        CancellationToken token)
    {
        if (Validate(version, documentJson) is { } code) return new(AdminStatus.ValidationFailed, null, code);
        if (string.IsNullOrWhiteSpace(provenance) || provenance.Length > 64) return new(AdminStatus.ValidationFailed, null, "PROVENANCE_REQUIRED");
        if (await context.Set<RequirementCatalogueVersionRecord>().AnyAsync(r => r.Version == version, token).ConfigureAwait(false))
            return new(AdminStatus.Conflict, null, "CATALOGUE_VERSION_EXISTS");
        var record = new RequirementCatalogueVersionRecord
        {
            Version = version, Status = "DRAFT", Provenance = provenance.Trim(), DocumentJson = documentJson, RowVersion = 1,
            CreatedBy = actor, CreatedAtUtc = Now(),
        };
        context.Add(record);
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        return AdminResult<CatalogueVersionView>.Ok(View(record));
    }

    public async Task<AdminResult<CatalogueVersionView>> CreateDraftAsync(Guid admin, string version, string provenance, string documentJson,
        CancellationToken token)
    {
        try
        {
            return await transactions.ExecuteAsync(async inner =>
            {
                var result = await AddDraftAsync(admin, version, provenance, documentJson, inner).ConfigureAwait(false);
                if (result.Status == AdminStatus.Success)
                    await AdminAudit.AppendAsync(context, admin, "CATALOGUE_DRAFT_CREATED", "REQUIREMENT_CATALOGUE", version, null,
                        new { provenance }, Now(), inner).ConfigureAwait(false);
                return result;
            }, token).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            return new(AdminStatus.Conflict, null, "CATALOGUE_VERSION_EXISTS");
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    public async Task<AdminResult<CatalogueVersionView>> TransitionAsync(Guid admin, string version, string target, long expectedRowVersion,
        DateTimeOffset? effectiveFromUtc, CancellationToken token)
    {
        try
        {
            return await transactions.ExecuteAsync(async inner =>
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT version FROM requirement_catalogue_versions WHERE version = {version} FOR UPDATE", inner).ConfigureAwait(false);
                var r = await context.Set<RequirementCatalogueVersionRecord>().SingleOrDefaultAsync(x => x.Version == version, inner).ConfigureAwait(false);
                if (r is null) return new AdminResult<CatalogueVersionView>(AdminStatus.NotFound, null, "NOT_FOUND");
                if (r.RowVersion != expectedRowVersion) return new(AdminStatus.Conflict, null, "STALE_CATALOGUE_VERSION");
                var now = Now();
                switch (target, r.Status)
                {
                    case ("PUBLISHED", "DRAFT"):
                        r.Status = "PUBLISHED"; r.PublishedBy = admin; r.PublishedAtUtc = now; r.EffectiveFromUtc = effectiveFromUtc ?? now;
                        break;
                    case ("RETIRED", "PUBLISHED"):
                        r.Status = "RETIRED"; r.RetiredAtUtc = now;
                        break;
                    default:
                        return new(AdminStatus.Conflict, null, "CATALOGUE_TRANSITION_NOT_ALLOWED");
                }
                r.RowVersion++;
                await context.SaveChangesAsync(inner).ConfigureAwait(false);
                await AdminAudit.AppendAsync(context, admin, "CATALOGUE_" + target, "REQUIREMENT_CATALOGUE", version, null,
                    new { r.EffectiveFromUtc }, now, inner).ConfigureAwait(false);
                return AdminResult<CatalogueVersionView>.Ok(View(r));
            }, token).ConfigureAwait(false);
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }
}

/// <summary>Serves the persistent catalogue; falls back to the labelled development file only when nothing is published.</summary>
internal sealed class PersistentRequirementCatalogueProvider(RequirementCatalogueStore store, ConfiguredRequirementCatalogueProvider file)
    : IRequirementCatalogueProvider
{
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<RequirementCatalogue?> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty) return null;
        var served = await store.GetServedDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (served is null) return await file.GetForProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<ConfiguredRequirementCatalogueProvider.CatalogueDocument>(served, Strict)?.ToCatalogue();
        }
        catch (JsonException)
        {
            return null; // fail closed: a corrupt published catalogue never silently falls back
        }
    }
}

/// <summary>Staff may propose a catalogue DRAFT; publication stays an admin-only action.</summary>
internal sealed class CatalogueDraftProposalApplier(TimeProvider time) : IProposalApplier
{
    public string TargetType => "REQUIREMENT_CATALOGUE_DRAFT";
    public string Module => AdminModules.RequirementCatalogue;

    public string? Validate(string changeType, string targetKey, string payloadJson) =>
        changeType != "CREATE" ? "CHANGE_TYPE_INVALID" : RequirementCatalogueStore.Validate(targetKey, payloadJson);

    public async Task<ProposalApplyOutcome> ApplyAsync(BuildMindDbContext context, StaffChangeProposalRecord proposal, Guid admin,
        DateTimeOffset now, CancellationToken token)
    {
        var store = new RequirementCatalogueStore(context, null!, time);
        var result = await store.AddDraftAsync(proposal.ProposedBy, proposal.TargetKey, "STAFF_PROPOSAL_UNVERIFIED", proposal.PayloadJson, token)
            .ConfigureAwait(false);
        return result.Status switch
        {
            AdminStatus.Success => ProposalApplyOutcome.Applied,
            AdminStatus.Conflict => ProposalApplyOutcome.Stale,
            _ => ProposalApplyOutcome.Invalid,
        };
    }
}
