using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BuildMind.API.Endpoints.Requirements;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Infrastructure.CaptainIntegration;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Requirements;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static BuildMind.Persistence.IntegrationTests.Captain.CaptainLiveSupport;

namespace BuildMind.Persistence.IntegrationTests.Captain;

public sealed class RequirementMutationHttpLiveTests
{
    private const string Scheme = "G12Test";
    private const string SubjectHeader = "X-G12-Test-Subject";
    private const string CatalogueVersion = "synthetic-fixture-v1";

    [Fact]
    public async Task H01_H05_FullMutationReplayConflictAndObservation()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);

        await using var app = await StartHostAsync(() => Context(db));

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(SubjectHeader, owner.ToString("D"));

        var route = $"/api/v1/projects/{project:D}/requirements";
        var operation = Guid.NewGuid();

        // H01: Initial mutation.
        using var first = await PutAsync(client, route, operation, 2);
        AssertStatus(first, HttpStatusCode.OK);
        var original = await ReadJsonAsync(first);
        var revision = original.GetProperty("revisionId").GetGuid();

        Assert.NotEqual(Guid.Empty, revision);
        Assert.Equal(project, original.GetProperty("projectId").GetGuid());
        Assert.Equal(1L, original.GetProperty("revisionNumber").GetInt64());

        // H02: Identical replay returns the original revision.
        using var replay = await PutAsync(client, route, operation, 2);
        AssertStatus(replay, HttpStatusCode.OK);
        var repeated = await ReadJsonAsync(replay);

        Assert.Equal(revision, repeated.GetProperty("revisionId").GetGuid());
        Assert.Equal(1L, repeated.GetProperty("revisionNumber").GetInt64());

        // H03: Same operation key with a different payload.
        using var conflict = await PutAsync(client, route, operation, 3);
        AssertStatus(conflict, HttpStatusCode.Conflict);
        var problem = await ReadJsonAsync(conflict);

        Assert.Equal("IDEMPOTENCY_KEY_REUSED",
            problem.GetProperty("code").GetString());

        // H04: Observe a committed operation.
        using var observed = await client.GetAsync(
            $"{route}/operations/{operation:D}");

        AssertStatus(observed, HttpStatusCode.OK);
        var committed = await ReadJsonAsync(observed);

        Assert.Equal("committed", committed.GetProperty("status").GetString());
        Assert.Equal(operation, committed.GetProperty("operationId").GetGuid());
        Assert.Equal(revision, committed.GetProperty("revisionId").GetGuid());

        // H05: Observe an operation that has never been submitted.
        var unknownOperation = Guid.NewGuid();

        using var absentResponse = await client.GetAsync(
            $"{route}/operations/{unknownOperation:D}");

        AssertStatus(absentResponse, HttpStatusCode.OK);
        var absent = await ReadJsonAsync(absentResponse);

        Assert.Equal("absentAtObservation",
            absent.GetProperty("status").GetString());

        Assert.Equal(unknownOperation,
            absent.GetProperty("operationId").GetGuid());

        Assert.False(absent.TryGetProperty("revisionId", out _));

        // Independent database verification.
        await using var observer = Context(db);

        var receipts = await observer.Set<RequirementMutationReceiptRecord>()
            .AsNoTracking().ToListAsync();

        var receipt = Assert.Single(receipts);

        Assert.Equal(project, receipt.ProjectId);
        Assert.Equal(owner, receipt.ActorId);
        Assert.Equal(operation, receipt.OperationId);
        Assert.Equal(revision, receipt.SavedRevisionId);

        var revisions = await observer.Set<RequirementRevisionRecord>()
            .AsNoTracking().ToListAsync();

        Assert.Single(revisions);

        var head = Assert.Single(await observer.Set<RequirementHeadRecord>()
            .AsNoTracking().ToListAsync());

        Assert.Equal(revision, head.CurrentRevisionId);
        Assert.Equal(1L, head.CurrentRevisionNumber);
    }


    [Fact]
    public async Task H06_H10_AuthorizationAndSectionReplay()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);
        await using var app = await StartHostAsync(() => Context(db));

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(SubjectHeader, owner.ToString("D"));

        var route = $"/api/v1/projects/{project:D}/requirements";
        var initialOperation = Guid.NewGuid();

        using var initial = await PutAsync(client, route, initialOperation, 2);
        AssertStatus(initial, HttpStatusCode.OK);

        var initialRevision = (await ReadJsonAsync(initial))
            .GetProperty("revisionId").GetGuid();

        // H06: Non-owner cannot mutate or observe the owner's operation.
        using var outsider = app.GetTestClient();
        outsider.DefaultRequestHeaders.Add(
            SubjectHeader, Guid.NewGuid().ToString("D"));

        using var deniedPut = await PutAsync(
            outsider, route, initialOperation, 2);

        AssertStatus(deniedPut, HttpStatusCode.NotFound);

        using var deniedObservation = await outsider.GetAsync(
            $"{route}/operations/{initialOperation:D}");

        AssertStatus(deniedObservation, HttpStatusCode.NotFound);

        // H07: Anonymous requests are rejected by authentication.
        using var anonymous = app.GetTestClient();

        using var anonymousPut = await PutAsync(
            anonymous, route, initialOperation, 2);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPut.StatusCode);

        using var anonymousObservation = await anonymous.GetAsync(
            $"{route}/operations/{initialOperation:D}");

        Assert.Equal(HttpStatusCode.Unauthorized,
            anonymousObservation.StatusCode);

        // H08: Spaces replacement and replay.
        var spacesOperation = Guid.NewGuid();

        var spacesPayload = new
        {
            expectedRevisionId = initialRevision,
            catalogueVersion = CatalogueVersion,
            additionalSpaces = Array.Empty<object>()
        };

        var spaces = await SaveAndReplaySectionAsync(
            client, $"{route}/spaces", spacesOperation,
            spacesPayload, 2);

        Assert.Equal(0, spaces.GetProperty("additionalSpaces").GetArrayLength());

        var spacesRevision = spaces.GetProperty("revisionId").GetGuid();
        Assert.NotEqual(initialRevision, spacesRevision);

        // H09: Relationships replacement and replay.
        var relationshipsOperation = Guid.NewGuid();

        var relationshipsPayload = new
        {
            expectedRevisionId = spacesRevision,
            catalogueVersion = CatalogueVersion,
            relationships = new[]
            {
                new
                {
                    sourceRoomType = "bedroom",
                    targetRoomType = "bathroom",
                    kind = "adjacent"
                }
            }
        };

        var relationships = await SaveAndReplaySectionAsync(
            client, $"{route}/relationships", relationshipsOperation,
            relationshipsPayload, 3);

        Assert.Equal(1,
            relationships.GetProperty("relationships").GetArrayLength());

        var relationshipsRevision =
            relationships.GetProperty("revisionId").GetGuid();

        Assert.NotEqual(spacesRevision, relationshipsRevision);

        // H10: Design method replacement and replay.
        var designOperation = Guid.NewGuid();

        var designPayload = new
        {
            expectedRevisionId = relationshipsRevision,
            catalogueVersion = CatalogueVersion,
            designMethod = "AI_GENERATED"
        };

        var design = await SaveAndReplaySectionAsync(
            client, $"/api/v1/projects/{project:D}/design-method",
            designOperation, designPayload, 4);

        var finalRevision = design.GetProperty("revisionId").GetGuid();

        Assert.NotEqual(relationshipsRevision, finalRevision);
        Assert.Equal("AI_GENERATED",
            design.GetProperty("designMethod").GetString());

        Assert.Equal(1, design.GetProperty("floorCount").GetInt32());
        Assert.Equal(4, design.GetProperty("groundFloorRooms").GetArrayLength());
        Assert.Equal(0, design.GetProperty("additionalSpaces").GetArrayLength());
        Assert.Equal(1, design.GetProperty("relationships").GetArrayLength());

        var bedroom = Assert.Single(
            design.GetProperty("groundFloorRooms").EnumerateArray(),
            room => room.GetProperty("roomType").GetString()
                == "bedroom");

        Assert.Equal(2, bedroom.GetProperty("count").GetInt32());

        var expectedReceipts = new Dictionary<Guid, Guid>
        {
            [initialOperation] = initialRevision,
            [spacesOperation] = spacesRevision,
            [relationshipsOperation] = relationshipsRevision,
            [designOperation] = finalRevision
        };

        // Historical operation observations must survive later revisions.
        foreach (var pair in expectedReceipts)
        {
            using var response = await client.GetAsync(
                $"{route}/operations/{pair.Key:D}");

            AssertStatus(response, HttpStatusCode.OK);

            var observation = await ReadJsonAsync(response);

            Assert.Equal("committed",
                observation.GetProperty("status").GetString());

            Assert.Equal(pair.Key,
                observation.GetProperty("operationId").GetGuid());

            Assert.Equal(pair.Value,
                observation.GetProperty("revisionId").GetGuid());
        }

        // Independent database verification.
        await using var observer = Context(db);

        var receipts = await observer.Set<RequirementMutationReceiptRecord>()
            .AsNoTracking().ToListAsync();

        Assert.Equal(4, receipts.Count);

        foreach (var receipt in receipts)
        {
            Assert.Equal(project, receipt.ProjectId);
            Assert.Equal(owner, receipt.ActorId);
            Assert.Equal(expectedReceipts[receipt.OperationId],
                receipt.SavedRevisionId);
        }

        var revisions = await observer.Set<RequirementRevisionRecord>()
            .AsNoTracking().ToListAsync();

        Assert.Equal(4, revisions.Count);

        var head = Assert.Single(
            await observer.Set<RequirementHeadRecord>()
                .AsNoTracking().ToListAsync());

        Assert.Equal(finalRevision, head.CurrentRevisionId);
        Assert.Equal(4L, head.CurrentRevisionNumber);
    }


    [Fact]
    public async Task H11_H12_LostAcknowledgementAndHttpReconciliation()
    {
        await using var db = await SchemaAsync();
        var (owner, project) = await SeedAsync(db);

        var operation = Guid.NewGuid();
        var fault = new HttpMutationLostAcknowledgement(operation);

        await using var app = await StartHostAsync(
            () => Context(db, fault));

        using var client = app.GetTestClient();

        client.DefaultRequestHeaders.Add(
            SubjectHeader, owner.ToString("D"));

        var route = $"/api/v1/projects/{project:D}/requirements";

        // H11: Commit succeeds but acknowledgement is lost.
        using var unknown = await PutAsync(
            client, route, operation, 2);

        AssertStatus(unknown, HttpStatusCode.ServiceUnavailable);

        var problem = await ReadJsonAsync(unknown);

        Assert.Equal(
            "REQUIREMENT_COMMIT_OUTCOME_UNKNOWN",
            problem.GetProperty("code").GetString());

        Assert.Equal(1, fault.InjectedFailures);

        Guid savedRevision;

        // Verify actual committed state independently.
        await using (var observer = Context(db))
        {
            var receipt = Assert.Single(
                await observer.Set<RequirementMutationReceiptRecord>()
                    .AsNoTracking().ToListAsync());

            var revision = Assert.Single(
                await observer.Set<RequirementRevisionRecord>()
                    .AsNoTracking().ToListAsync());

            var head = Assert.Single(
                await observer.Set<RequirementHeadRecord>()
                    .AsNoTracking().ToListAsync());

            Assert.Equal(project, receipt.ProjectId);
            Assert.Equal(owner, receipt.ActorId);
            Assert.Equal(operation, receipt.OperationId);
            Assert.Equal(1L, receipt.AuthorizationVersion);

            savedRevision = receipt.SavedRevisionId;

            Assert.NotEqual(Guid.Empty, savedRevision);
            Assert.Equal(savedRevision, revision.Id);
            Assert.Equal(savedRevision, head.CurrentRevisionId);
            Assert.Equal(1L, head.CurrentRevisionNumber);
        }

        // H12: Reconcile through a new HTTP request.
        using var recoveryClient = app.GetTestClient();

        recoveryClient.DefaultRequestHeaders.Add(
            SubjectHeader, owner.ToString("D"));

        using var observed = await recoveryClient.GetAsync(
            $"{route}/operations/{operation:D}");

        AssertStatus(observed, HttpStatusCode.OK);

        var observation = await ReadJsonAsync(observed);

        Assert.Equal("committed",
            observation.GetProperty("status").GetString());

        Assert.Equal(operation,
            observation.GetProperty("operationId").GetGuid());

        Assert.Equal(savedRevision,
            observation.GetProperty("revisionId").GetGuid());

        // Explicit caller retry must recover the same revision.
        using var replay = await PutAsync(
            recoveryClient, route, operation, 2);

        AssertStatus(replay, HttpStatusCode.OK);

        var recovered = await ReadJsonAsync(replay);

        Assert.Equal(savedRevision,
            recovered.GetProperty("revisionId").GetGuid());

        Assert.Equal(1L,
            recovered.GetProperty("revisionNumber").GetInt64());

        Assert.Equal(1, fault.InjectedFailures);

        // No duplicate persistence after reconciliation and replay.
        await using var final = Context(db);

        var finalReceipt = Assert.Single(
            await final.Set<RequirementMutationReceiptRecord>()
                .AsNoTracking().ToListAsync());

        var finalRevision = Assert.Single(
            await final.Set<RequirementRevisionRecord>()
                .AsNoTracking().ToListAsync());

        var finalHead = Assert.Single(
            await final.Set<RequirementHeadRecord>()
                .AsNoTracking().ToListAsync());

        Assert.Equal(operation, finalReceipt.OperationId);
        Assert.Equal(savedRevision, finalReceipt.SavedRevisionId);
        Assert.Equal(savedRevision, finalRevision.Id);
        Assert.Equal(savedRevision, finalHead.CurrentRevisionId);
        Assert.Equal(1L, finalHead.CurrentRevisionNumber);
    }

    private sealed class HttpMutationLostAcknowledgement(
        Guid operation)
        : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        private int injectedFailures;

        public int InjectedFailures =>
            Volatile.Read(ref injectedFailures);

        public override Task TransactionCommittedAsync(
            System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            var matchingReceipt =
                eventData.Context?.ChangeTracker
                    .Entries<RequirementMutationReceiptRecord>()
                    .Any(entry =>
                        entry.Entity.OperationId == operation &&
                        entry.State == EntityState.Unchanged)
                == true;

            if (matchingReceipt &&
                Interlocked.CompareExchange(
                    ref injectedFailures, 1, 0) == 0)
            {
                throw new IOException(
                    "Synthetic HTTP mutation acknowledgement loss after commit.");
            }

            return Task.CompletedTask;
        }
    }

    private static async Task<JsonElement> SaveAndReplaySectionAsync(
        HttpClient client, string route, Guid operation,
        object payload, long expectedNumber)
    {
        using var first = await SendSectionAsync(
            client, route, operation, payload);

        AssertStatus(first, HttpStatusCode.OK);

        var saved = await ReadJsonAsync(first);

        Assert.Equal(expectedNumber,
            saved.GetProperty("revisionNumber").GetInt64());

        using var replay = await SendSectionAsync(
            client, route, operation, payload);

        AssertStatus(replay, HttpStatusCode.OK);

        var repeated = await ReadJsonAsync(replay);

        Assert.Equal(saved.GetProperty("revisionId").GetGuid(),
            repeated.GetProperty("revisionId").GetGuid());

        Assert.Equal(expectedNumber,
            repeated.GetProperty("revisionNumber").GetInt64());

        return saved;
    }

    private static async Task<HttpResponseMessage> SendSectionAsync(
        HttpClient client, string route, Guid operation, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, route);

        request.Headers.Add("Idempotency-Key", operation.ToString("D"));
        request.Content = JsonContent.Create(payload);

        return await client.SendAsync(request);
    }


    private static async Task<WebApplication> StartHostAsync(
        Func<BuildMindDbContext> contextFactory)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" });

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Configuration["CaptainIntegration:RequirementCatalogueFile"] =
            CataloguePath();

        builder.Services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                Scheme, _ => { });

        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

        builder.Services.AddScoped<BuildMindDbContext>(_ => contextFactory());
        builder.Services.AddScoped<TransactionRunner>(sp =>
            Transactions(sp.GetRequiredService<BuildMindDbContext>()));

        builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
        builder.Services.AddCaptainIntegrationInfrastructure();

        builder.Services.AddScoped<RequirementRevisionReader>();
        builder.Services.AddScoped<ValidatedRequirementRevisionWriter>();

        builder.Services.AddScoped<IRequirementMutationCoordinator,
            RequirementMutationCoordinator>();

        builder.Services.AddScoped<IRequirementsApplicationService>(sp =>
            ActivatorUtilities.CreateInstance<RequirementsApplicationService>(
                sp, sp.GetRequiredService<IProjectAuthorizationPort>()));

        builder.Services.AddScoped<IRequirementsSectionApplicationService,
            RequirementsSectionApplicationService>();

        builder.Services.AddSingleton<IApiEndpointModule,
            RequirementsEndpointModule>();

        builder.Services.AddSingleton<IApiEndpointModule,
            RequirementsSectionsEndpointModule>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapBuildMindApiV1();

        await app.StartAsync();

        return app;
    }

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client, string route, Guid operation, int bedrooms)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, route);

        request.Headers.Add("Idempotency-Key", operation.ToString("D"));

        request.Content = JsonContent.Create(new
        {
            expectedRevisionId = (Guid?)null,
            catalogueVersion = CatalogueVersion,
            floorCount = 1,
            groundFloorRooms = new[]
            {
                new { roomType = "bedroom", count = bedrooms },
                new { roomType = "livingRoom", count = 1 },
                new { roomType = "kitchen", count = 1 },
                new { roomType = "bathroom", count = 1 }
            },
            additionalSpaces = Array.Empty<object>(),
            relationships = Array.Empty<object>(),
            designMethod = (string?)null
        });

        return await client.SendAsync(request);
    }

    private static void AssertStatus(
        HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    private static string CataloguePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "BuildMind.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
            throw new InvalidOperationException("Repository root not found.");

        var path = Path.Combine(directory.FullName, "docs",
            "captain-development", "samples",
            "requirement-catalogue.synthetic.json");

        if (!File.Exists(path))
            throw new FileNotFoundException("Synthetic catalogue missing.", path);

        return path;
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(SubjectHeader, out var values) ||
                values.Count != 1 ||
                !Guid.TryParse(values[0], out var actor) ||
                actor == Guid.Empty)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actor.ToString("D"))],
                Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}