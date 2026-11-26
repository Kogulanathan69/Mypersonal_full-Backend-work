using System.Security.Cryptography;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.Planning;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.CaptainIntegration;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records.Survey;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Repositories.Survey;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using static BuildMind.Persistence.IntegrationTests.Captain.CaptainLiveSupport;
using SurveyEntity = BuildMind.Domain.Entities.Survey;

namespace BuildMind.Persistence.IntegrationTests.Captain;

/// <summary>Durable survey extraction on disposable MySQL 8.4: leased claims, recovery, publication, fail-closed reads.</summary>
public sealed class SurveyExtractionLiveTests : IDisposable
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(3);
    private readonly string storage = Directory.CreateTempSubdirectory("bm-survey-live-").FullName;

    public void Dispose() => Directory.Delete(storage, recursive: true);

    [Fact]
    public async Task Q01_concurrent_claims_of_one_queued_survey_have_exactly_one_winner()
    {
        await using var db = await SchemaAsync();
        var survey = await QueuedAsync(db, [1, 2, 3]);
        var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var context = Context(db);
            return await Queue(context).TryClaimAsync(survey.Id, Now, Lease, 3, default);
        }));
        Assert.Single(claims, c => c is not null);
        await using var check = Context(db);
        var row = await check.Set<SurveyRecord>().AsNoTracking().SingleAsync(s => s.Id == survey.Id);
        Assert.Equal((nameof(SurveyStatus.Processing), 2L, 1), (row.Status, row.StateVersion, row.ProcessingAttempts));
    }

    [Fact]
    public async Task Q02_expired_lease_is_reclaimed_and_the_stale_claimer_can_no_longer_publish()
    {
        await using var db = await SchemaAsync();
        var survey = await QueuedAsync(db, [1, 2, 3]);
        await using var context = Context(db);
        var first = (await Queue(context).TryClaimAsync(survey.Id, Now, Lease, 3, default))!;
        Assert.Null(await Queue(context).TryClaimAsync(survey.Id, Now.AddMinutes(1), Lease, 3, default)); // lease still valid
        var second = (await Queue(context).TryClaimAsync(survey.Id, Now.AddMinutes(4), Lease, 3, default))!;
        Assert.Equal(2, second.Attempt);

        Assert.False(await Queue(context).TryPublishAsync(first, Extraction(survey), true, null, Now.AddMinutes(5), default));
        Assert.True(await Queue(context).TryPublishAsync(second, Extraction(survey), true, null, Now.AddMinutes(5), default));
        Assert.False(await Queue(context).TryPublishAsync(second, Extraction(survey), true, null, Now.AddMinutes(5), default));
        await using var check = Context(db);
        Assert.Equal(1, await check.Set<SurveyExtractionRecord>().CountAsync(e => e.SurveyId == survey.Id));
        Assert.Equal(nameof(SurveyStatus.Completed), (await check.Set<SurveyRecord>().AsNoTracking().SingleAsync(s => s.Id == survey.Id)).Status);
    }

    [Fact]
    public async Task Q03_exhausted_attempts_fail_the_survey_as_abandoned()
    {
        await using var db = await SchemaAsync();
        var survey = await QueuedAsync(db, [1]);
        await using var context = Context(db);
        Assert.NotNull(await Queue(context).TryClaimAsync(survey.Id, Now, Lease, 1, default));
        Assert.Null(await Queue(context).TryClaimAsync(survey.Id, Now.AddMinutes(10), Lease, 1, default));
        var row = await context.Set<SurveyRecord>().AsNoTracking().SingleAsync(s => s.Id == survey.Id);
        Assert.Equal((nameof(SurveyStatus.Failed), "EXTRACTION_ABANDONED"), (row.Status, row.FailureCode));
    }

    [Fact]
    public async Task Q04_revoked_authorization_fails_publication_without_storing_extraction()
    {
        await using var db = await SchemaAsync();
        var survey = await QueuedAsync(db, [1]);
        await using var context = Context(db);
        var claim = (await Queue(context).TryClaimAsync(survey.Id, Now, Lease, 3, default))!;
        await using (var other = Context(db))
            await other.Database.ExecuteSqlInterpolatedAsync($"UPDATE projects SET owner_id = {(await NewUserAsync(db))} WHERE id = {survey.ProjectId}");

        Assert.True(await Queue(context).TryPublishAsync(claim, Extraction(survey), true, null, Now, default));
        var row = await context.Set<SurveyRecord>().AsNoTracking().SingleAsync(s => s.Id == survey.Id);
        Assert.Equal((nameof(SurveyStatus.Failed), "PROJECT_ACCESS_REVOKED"), (row.Status, row.FailureCode));
        Assert.Equal(0, await context.Set<SurveyExtractionRecord>().CountAsync());
    }

    [Theory]
    [InlineData("missing", "SURVEY_FILE_MISSING")]
    [InlineData("corrupt", "SURVEY_FILE_INTEGRITY_MISMATCH")]
    [InlineData("truncated", "SURVEY_FILE_SIZE_MISMATCH")]
    public async Task P01_unsafe_or_damaged_files_fail_closed_without_calling_python(string damage, string code)
    {
        await using var db = await SchemaAsync();
        var content = new byte[] { 0x25, 0x50, 0x44, 0x46, 1, 2, 3, 4 };
        var survey = await QueuedAsync(db, content, write: damage != "missing");
        var path = Path.Combine(storage, survey.StorageId.ToString("N") + ".blob");
        if (damage == "corrupt") await File.WriteAllBytesAsync(path, [0x25, 0x50, 0x44, 0x46, 9, 9, 9, 9]);
        if (damage == "truncated") await File.WriteAllBytesAsync(path, [0x25, 0x50]);
        var client = new FakeExtraction();
        await using var context = Context(db);

        Assert.Equal("FAILED", await Processor(context, client).ProcessAsync(survey.Id, Options(), default));
        Assert.Equal(0, client.Calls);
        Assert.Equal(code, (await context.Set<SurveyRecord>().AsNoTracking().SingleAsync(s => s.Id == survey.Id)).FailureCode);
    }

    [Fact]
    public async Task P02_needs_review_completes_processing_and_stores_evidence_separately()
    {
        await using var db = await SchemaAsync();
        var survey = await QueuedAsync(db, [0x25, 0x50, 0x44, 0x46, 5, 6]);
        var client = new FakeExtraction { Outcome = "NEEDS_REVIEW" };
        await using var context = Context(db);

        Assert.Equal("NEEDS_REVIEW", await Processor(context, client).ProcessAsync(survey.Id, Options(), default));
        Assert.Equal(1, client.Calls);
        var row = await context.Set<SurveyRecord>().AsNoTracking().SingleAsync(s => s.Id == survey.Id);
        Assert.Equal(nameof(SurveyStatus.Completed), row.Status);
        var extraction = await new MySqlSurveyExtractionReader(context).GetAsync(survey.ProjectId, survey.Id, default);
        Assert.Equal(("NEEDS_REVIEW", "OCR"), (extraction!.Outcome, extraction.Method));
        Assert.Null(await new MySqlSurveyExtractionReader(context).GetAsync(Guid.NewGuid(), survey.Id, default)); // project-scoped
    }

    [Fact]
    public async Task P03_dependency_unavailable_defers_without_state_change_and_retry_succeeds()
    {
        await using var db = await SchemaAsync();
        var survey = await QueuedAsync(db, [0x25, 0x50, 0x44, 0x46]);
        var client = new FakeExtraction { Failure = new DependencyFailureException("python", DependencyFailureKind.Unavailable) };
        await using var context = Context(db);

        Assert.Equal("DEFERRED", await Processor(context, client).ProcessAsync(survey.Id, Options(), default));
        var row = await context.Set<SurveyRecord>().AsNoTracking().SingleAsync(s => s.Id == survey.Id);
        Assert.Equal((nameof(SurveyStatus.Processing), 1), (row.Status, row.ProcessingAttempts));
        // After the lease expires a later attempt reclaims it and publishes.
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE surveys SET processing_lease_until_utc = {DateTime.UtcNow.AddMinutes(-1)} WHERE id = {survey.Id}");
        client.Failure = null;
        Assert.Equal("COMPLETED", await Processor(context, client).ProcessAsync(survey.Id, Options(), default));
    }

    private static SurveyWorkerOptions Options() => new(TimeSpan.FromSeconds(1), 1, Lease, 3, TimeSpan.FromSeconds(30));

    private static SurveyWorkQueue Queue(BuildMindDbContext context) =>
        new(context, Transactions(context), new ProjectOwnerAuthorizationPort(context));

    private SurveyExtractionProcessor Processor(BuildMindDbContext context, ISurveyExtractionClient client) =>
        new(Queue(context), new SurveyBlobReader(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { [CaptainIntegrationInfrastructure.SurveyStorageDirectoryKey] = storage }).Build()),
            client, new ProjectOwnerAuthorizationPort(context), TimeProvider.System, NullLogger<SurveyExtractionProcessor>.Instance);

    private async Task<SurveyEntity> QueuedAsync(Support.DisposableMySqlDatabase db, byte[] content, bool write = true)
    {
        var (owner, project) = await SeedAsync(db);
        var survey = SurveyEntity.CreateQueued(Guid.NewGuid(), project, owner, Guid.NewGuid(), "synthetic.pdf", "application/pdf",
            content.Length, Convert.ToHexStringLower(SHA256.HashData(content)), Now);
        if (write) await File.WriteAllBytesAsync(Path.Combine(storage, survey.StorageId.ToString("N") + ".blob"), content);
        await using var context = Context(db);
        context.Add(SurveyRepository.ToRecord(survey));
        await context.SaveChangesAsync();
        return survey;
    }

    private static async Task<Guid> NewUserAsync(Support.DisposableMySqlDatabase db)
    {
        var (other, _) = await SeedAsync(db);
        return other;
    }

    private static SurveyExtractionRecord Extraction(SurveyEntity survey) => new()
    {
        ExtractionId = Guid.NewGuid(), SurveyId = survey.Id, ProjectId = survey.ProjectId, Outcome = "COMPLETED", Method = "PDF_TEXT",
        ResultJson = "{}", CreatedAtUtc = Now,
    };

    private sealed class FakeExtraction : ISurveyExtractionClient
    {
        public int Calls { get; private set; }
        public string Outcome { get; init; } = "COMPLETED";
        public Exception? Failure { get; set; }
        public TimeSpan Delay { get; init; }

        public async Task<SurveyExtractionOutput> ExtractAsync(SurveyExtractionInput input, CancellationToken token)
        {
            Calls++;
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, token);
            if (Failure is not null) throw Failure;
            return new SurveyExtractionOutput(Outcome, "OCR",
                $$"""{"extraction_id":"{{input.ExtractionId}}","file_id":"{{input.FileId}}","status":"{{Outcome}}","measurements":[],"diagnostics":[]}""");
        }
    }

    [Fact]
    public async Task H01_heartbeat_keeps_a_long_extraction_from_being_reclaimed()
    {
        await using var db = await SchemaAsync();
        var survey = await QueuedAsync(db, [0x25, 0x50, 0x44, 0x46, 7]);
        var options = new SurveyWorkerOptions(TimeSpan.FromSeconds(1), 1, TimeSpan.FromSeconds(3), 3, TimeSpan.FromSeconds(30));
        await using var context = Context(db);
        var work = Processor(context, new FakeExtraction { Delay = TimeSpan.FromSeconds(7) }).ProcessAsync(survey.Id, options, default);
        await Task.Delay(TimeSpan.FromSeconds(5)); // beyond the initial 3 s lease
        await using (var competitor = Context(db))
            Assert.Null(await Queue(competitor).TryClaimAsync(survey.Id, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(3), 3, default));
        Assert.Equal("COMPLETED", await work);
    }

    [Fact]
    public async Task H02_a_worker_whose_lease_was_taken_over_discards_its_result()
    {
        await using var db = await SchemaAsync();
        var survey = await QueuedAsync(db, [0x25, 0x50, 0x44, 0x46, 8]);
        var options = new SurveyWorkerOptions(TimeSpan.FromSeconds(1), 1, TimeSpan.FromSeconds(30), 3, TimeSpan.FromSeconds(60));
        await using var context = Context(db);
        var slow = new FakeExtraction { Delay = TimeSpan.FromSeconds(15) };
        var work = Processor(context, slow).ProcessAsync(survey.Id, options with { Lease = TimeSpan.FromSeconds(9) }, default);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await using (var other = Context(db))
        {
            // Simulate the first worker stalling: its lease expires and another worker reclaims the survey.
            await other.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE surveys SET processing_lease_until_utc = {DateTime.UtcNow.AddMinutes(-1)} WHERE id = {survey.Id}");
            Assert.NotNull(await Queue(other).TryClaimAsync(survey.Id, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), 3, default));
        }
        Assert.Equal("LOST", await work);
        await using var check = Context(db);
        Assert.Equal(0, await check.Set<SurveyExtractionRecord>().CountAsync(e => e.SurveyId == survey.Id));
        Assert.Equal(nameof(SurveyStatus.Processing), (await check.Set<SurveyRecord>().AsNoTracking().SingleAsync(s => s.Id == survey.Id)).Status);
    }

    [Fact]
    public async Task R01_deferred_delete_keeps_blobs_and_reconciliation_removes_only_old_unreferenced_ones()
    {
        await using var db = await SchemaAsync();
        var referenced = await QueuedAsync(db, [0x25, 0x50, 0x44, 0x46, 9]);
        var referencedPath = Path.Combine(storage, referenced.StorageId.ToString("N") + ".blob");
        var oldOrphan = Path.Combine(storage, Guid.NewGuid().ToString("N") + ".blob");
        var recentOrphan = Path.Combine(storage, Guid.NewGuid().ToString("N") + ".blob");
        await File.WriteAllBytesAsync(oldOrphan, [1]);
        await File.WriteAllBytesAsync(recentOrphan, [2]);
        File.SetLastWriteTimeUtc(oldOrphan, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(referencedPath, DateTime.UtcNow.AddHours(-2));

        var deferred = new DeferredDeleteSurveyStorage(new BuildMind.Infrastructure.Survey.PrivateSurveyFileStorage(storage),
            NullLogger<DeferredDeleteSurveyStorage>.Instance);
        await deferred.DeleteAsync(referenced.StorageId, default); // the uncertain-commit cleanup path
        Assert.True(File.Exists(referencedPath));

        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [CaptainIntegrationInfrastructure.SurveyStorageDirectoryKey] = storage }).Build();
        await using var context = Context(db);
        var report = await new SurveyStorageReconciler(context, config, TimeProvider.System).ReconcileAsync(TimeSpan.FromMinutes(30), 100, default);
        Assert.Equal((3, 1, 1, 1), (report.Scanned, report.Referenced, report.TooRecent, report.Deleted));
        Assert.True(File.Exists(referencedPath));
        Assert.True(File.Exists(recentOrphan));
        Assert.False(File.Exists(oldOrphan));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new SurveyStorageReconciler(context, config, TimeProvider.System).ReconcileAsync(TimeSpan.FromMinutes(1), 100, default));
    }
}
