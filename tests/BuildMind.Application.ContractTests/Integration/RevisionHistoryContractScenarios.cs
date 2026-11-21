using System.Text.Json;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

/// <summary>
/// Database-free STEP 11-B security regression scenarios. They prove application-boundary behavior only;
/// database predicates, authorization locking and snapshot immutability remain M4 persistence obligations.
/// </summary>
internal static class RevisionHistoryContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("RH-01 authorized owner receives bounded revision history", () =>
        {
            var context = Context();
            var revisions = History(context.ProjectId, context.PlanId, 3);
            context.Port.PageResult = Page(revisions, false);

            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 3)).Result;

            Check(result.Status == PlanRevisionHistoryStatus.Succeeded && result.Revisions?.Count == 3,
                "Authorized history was not returned.");
            Check(context.Authorization.Calls == 2 && context.Port.PageCalls == 1,
                "History did not use pre/post authorization and one bounded read.");
            Check(context.Port.LastProjectId == context.ProjectId && context.Port.LastPlanId == context.PlanId &&
                context.Port.LastPageSize == 3 && context.Port.LastCursor is null,
                "List query did not forward its exact bounded scope.");
        });

        run("RH-02 authorized owner receives exact revision", () =>
        {
            var context = Context();
            var revision = History(context.ProjectId, context.PlanId, 1)[0];
            context.Port.RevisionResult = new(PlanRevisionHistorySourceStatus.Succeeded, revision);

            var result = context.Service.GetAsync(context.ProjectId, context.PlanId, revision.RevisionId).Result;

            Check(result.Status == PlanRevisionHistoryStatus.Succeeded && result.Revision == revision,
                "Exact authorized revision was not returned.");
            Check(context.Port.LastRevisionId == revision.RevisionId &&
                context.Port.LastProjectId == context.ProjectId && context.Port.LastPlanId == context.PlanId,
                "Exact lookup did not forward its exact scope.");
        });

        run("RH-03 empty authorized history is safe", () =>
        {
            var context = Context();
            context.Port.PageResult = new(PlanRevisionHistorySourceStatus.Succeeded, [], false);

            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;

            Check(result.Status == PlanRevisionHistoryStatus.Succeeded && result.Revisions is { Count: 0 } &&
                result.NextCursor is null, "Empty history was not represented safely.");
        });

        run("RH-04 invalid authenticated actor fails closed", () =>
        {
            var context = Context(authenticated: false);
            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;

            CheckDenied(result.Status, result.Revisions);
            Check(context.Authorization.Calls == 0 && context.Port.TotalCalls == 0,
                "Invalid actor reached protected dependencies.");
        });

        run("RH-04B empty project plan and revision identifiers fail before dependencies", () =>
        {
            var context = Context();
            var listProject = context.Service.ListAsync(new(Guid.Empty, context.PlanId, 10)).Result;
            var listPlan = context.Service.ListAsync(new(context.ProjectId, Guid.Empty, 10)).Result;
            var lookupRevision = context.Service.GetAsync(
                context.ProjectId, context.PlanId, Guid.Empty).Result;

            Check(listProject.Status == PlanRevisionHistoryStatus.InvalidRequest &&
                listPlan.Status == PlanRevisionHistoryStatus.InvalidRequest &&
                lookupRevision.Status == PlanRevisionHistoryStatus.InvalidRequest &&
                context.Authorization.Calls == 0 && context.Port.TotalCalls == 0,
                "Empty identifiers reached protected dependencies.");
        });

        run("RH-05 non-owner denial discloses no history", () =>
        {
            var context = Context();
            context.Authorization.Results.Enqueue(new(ProjectAuthorizationStatus.Denied));

            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;

            CheckDenied(result.Status, result.Revisions);
            Check(context.Port.TotalCalls == 0, "Non-owner reached the history source.");
        });

        run("RH-06 wrong project result fails closed", () =>
        {
            var context = Context();
            var revision = History(context.ProjectId, context.PlanId, 1)[0] with { ProjectId = Guid.NewGuid() };
            context.Port.PageResult = Page([revision], false);

            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;

            CheckInvalid(result.Status, result.Revisions);
        });

        run("RH-07 wrong plan result fails closed", () =>
        {
            var context = Context();
            var revision = History(context.ProjectId, context.PlanId, 1)[0] with { PlanId = Guid.NewGuid() };
            context.Port.PageResult = Page([revision], false);

            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;

            CheckInvalid(result.Status, result.Revisions);
        });

        run("RH-08 missing revision is indistinguishable from denied access", () =>
        {
            var context = Context();
            context.Port.RevisionResult = new(PlanRevisionHistorySourceStatus.NotFound);

            var result = context.Service.GetAsync(
                context.ProjectId, context.PlanId, Guid.NewGuid()).Result;

            Check(result.Status == PlanRevisionHistoryStatus.DeniedOrNotFound && result.Revision is null,
                "Missing revision disclosed a distinct protected-resource outcome.");
        });

        run("RH-09 cross-project exact revision substitution is rejected", () =>
        {
            var context = Context();
            var requested = Guid.NewGuid();
            var revision = Revision(Guid.NewGuid(), context.PlanId, requested, 1, null);
            context.Port.RevisionResult = new(PlanRevisionHistorySourceStatus.Succeeded, revision);

            var result = context.Service.GetAsync(context.ProjectId, context.PlanId, requested).Result;

            CheckInvalid(result.Status, result.Revision);
        });

        run("RH-10 cross-plan exact revision substitution is rejected", () =>
        {
            var context = Context();
            var requested = Guid.NewGuid();
            var revision = Revision(context.ProjectId, Guid.NewGuid(), requested, 1, null);
            context.Port.RevisionResult = new(PlanRevisionHistorySourceStatus.Succeeded, revision);

            var result = context.Service.GetAsync(context.ProjectId, context.PlanId, requested).Result;

            CheckInvalid(result.Status, result.Revision);
        });

        run("RH-11 corrupted geometry hash is rejected", () =>
        {
            var context = Context();
            var revision = History(context.ProjectId, context.PlanId, 1)[0] with { GeometryHash = "sha256:corrupt" };
            context.Port.RevisionResult = new(PlanRevisionHistorySourceStatus.Succeeded, revision);

            var result = context.Service.GetAsync(
                context.ProjectId, context.PlanId, revision.RevisionId).Result;

            CheckInvalid(result.Status, result.Revision);
        });

        run("RH-12 invalid validation reference is rejected", () =>
        {
            var context = Context();
            var revision = History(context.ProjectId, context.PlanId, 1)[0];
            revision = revision with
            {
                Validation = new PlanValidationReferenceDto
                {
                    ValidationId = Guid.Empty,
                    ValidatorVersion = "test-only/1",
                    GeometryHash = revision.GeometryHash,
                    GeometryValid = true,
                    RequirementsMatch = true,
                    RegulatoryEvidenceStatus = "VERIFIED",
                    HasBlockingIssues = false,
                    ValidatedAtUtc = DateTimeOffset.UtcNow
                }
            };
            context.Port.RevisionResult = new(PlanRevisionHistorySourceStatus.Succeeded, revision);

            var result = context.Service.GetAsync(
                context.ProjectId, context.PlanId, revision.RevisionId).Result;

            CheckInvalid(result.Status, result.Revision);
        });

        run("RH-13 invalid revision version is rejected", () =>
        {
            var context = Context();
            var revision = History(context.ProjectId, context.PlanId, 1)[0] with { VersionNumber = 0 };
            context.Port.RevisionResult = new(PlanRevisionHistorySourceStatus.Succeeded, revision);

            var result = context.Service.GetAsync(
                context.ProjectId, context.PlanId, revision.RevisionId).Result;

            CheckInvalid(result.Status, result.Revision);
        });

        run("RH-14 contradictory parent lineage is rejected", () =>
        {
            var context = Context();
            var revisions = History(context.ProjectId, context.PlanId, 3).ToArray();
            revisions[0] = revisions[0] with { ParentRevisionId = Guid.NewGuid() };
            context.Port.PageResult = Page(revisions, false);

            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 3)).Result;

            CheckInvalid(result.Status, result.Revisions);
        });

        run("RH-15 ordering is deterministic and descending", () =>
        {
            var context = Context();
            var revisions = History(context.ProjectId, context.PlanId, 3).ToArray();
            context.Port.PageResult = Page(revisions, false);
            var success = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 3)).Result;
            Check(success.Revisions!.Select(item => item.VersionNumber).SequenceEqual([3L, 2L, 1L]),
                "Successful history order changed.");

            context.Port.PageResult = Page([revisions[1], revisions[0], revisions[2]], false);
            var unordered = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 3)).Result;
            CheckInvalid(unordered.Status, unordered.Revisions);
        });

        run("RH-16 maximum page size is enforced before repository access", () =>
        {
            var context = Context();
            var result = context.Service.ListAsync(new(
                context.ProjectId, context.PlanId, PlanRevisionHistoryService.MaximumPageSize + 1)).Result;

            Check(result.Status == PlanRevisionHistoryStatus.InvalidRequest && context.Port.TotalCalls == 0,
                "Oversized page reached the repository.");
            Check(PlanRevisionHistoryService.MaximumPageSize == 50,
                "The conservative public application bound changed.");
        });

        run("RH-17 invalid or manipulated cursor is rejected", () =>
        {
            var context = Context();
            var invalid = new PlanRevisionHistoryCursor(
                Guid.Empty, Guid.Empty, 0, Guid.Empty, 0, Guid.Empty, Guid.Empty);
            var rejected = context.Service.ListAsync(new(
                context.ProjectId, context.PlanId, 10, invalid)).Result;
            Check(rejected.Status == PlanRevisionHistoryStatus.InvalidCursor && context.Port.TotalCalls == 0,
                "Structurally invalid cursor reached the source.");

            var revisions = History(context.ProjectId, context.PlanId, 2);
            var anchorId = Guid.NewGuid();
            var cursor = new PlanRevisionHistoryCursor(
                context.ProjectId, context.PlanId, 3, anchorId, 3, anchorId, Guid.NewGuid());
            context.Port.PageResult = Page(revisions, false,
                new PlanRevisionHistoryHead(cursor.HeadVersionNumber, cursor.HeadRevisionId), true);
            var manipulated = context.Service.ListAsync(new(
                context.ProjectId, context.PlanId, 10, cursor)).Result;
            CheckInvalid(manipulated.Status, manipulated.Revisions);

            var validContext = Context();
            var complete = History(validContext.ProjectId, validContext.PlanId, 3);
            validContext.Port.PageResult = Page([complete[0]], true);
            var first = validContext.Service.ListAsync(new(
                validContext.ProjectId, validContext.PlanId, 1)).Result;
            Check(first.Status == PlanRevisionHistoryStatus.Succeeded && first.NextCursor is { } issued &&
                issued.AfterRevisionId == complete[0].RevisionId &&
                issued.ExpectedNextRevisionId == complete[1].RevisionId,
                "First page did not issue the exact lineage cursor.");

            validContext.Port.ExpectedCursor = first.NextCursor;
            validContext.Port.PageResult = Page(
                [complete[1], complete[2]], false,
                new PlanRevisionHistoryHead(complete[0].VersionNumber, complete[0].RevisionId), true);
            var second = validContext.Service.ListAsync(new(
                validContext.ProjectId, validContext.PlanId, 2, first.NextCursor)).Result;
            Check(second.Status == PlanRevisionHistoryStatus.Succeeded && second.Revisions?.Count == 2 &&
                second.NextCursor is null, "Valid cursor did not continue the exact lineage.");
        });

        run("RH-18 repository unavailability is safely classified", () =>
        {
            var context = Context();
            context.Port.PageFailure = new HttpRequestException("private destination");
            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;
            Check(result.Status == PlanRevisionHistoryStatus.DependencyUnavailable && result.Revisions is null,
                "Repository unavailability escaped or disclosed data.");
        });

        run("RH-19 repository timeout is safely classified", () =>
        {
            var context = Context();
            context.Port.PageFailure = new TimeoutException("private timeout");
            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;
            Check(result.Status == PlanRevisionHistoryStatus.DependencyTimeout && result.Revisions is null,
                "Repository timeout escaped or disclosed data.");
        });

        run("RH-20 malformed repository results fail closed", () =>
        {
            foreach (var malformed in new PlanRevisionHistorySourcePage?[]
            {
                null,
                new(PlanRevisionHistorySourceStatus.Succeeded, null, false),
                new((PlanRevisionHistorySourceStatus)999, null, false),
                new(PlanRevisionHistorySourceStatus.NotFound, [], false),
                new(PlanRevisionHistorySourceStatus.Succeeded, [], true)
            })
            {
                var context = Context();
                context.Port.PageResult = malformed;
                var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;
                CheckInvalid(result.Status, result.Revisions);
            }
        });

        run("RH-21 caller cancellation propagates without protected reads", () =>
        {
            var context = Context();
            using var source = new CancellationTokenSource();
            source.Cancel();
            CheckThrows<OperationCanceledException>(() => context.Service.ListAsync(
                new(context.ProjectId, context.PlanId, 10), source.Token).GetAwaiter().GetResult());
            Check(context.Port.TotalCalls == 0, "Canceled request reached the history source.");
        });

        run("RH-22 authorization revocation between checks withholds snapshots", () =>
        {
            var context = Context();
            var revisions = History(context.ProjectId, context.PlanId, 1);
            context.Port.PageResult = Page(revisions, false);
            context.Authorization.Results.Enqueue(Authorized(context.ProjectId, context.ActorId, 1));
            context.Authorization.Results.Enqueue(new(ProjectAuthorizationStatus.Denied));

            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;

            CheckDenied(result.Status, result.Revisions);
            Check(context.Port.PageCalls == 1 && context.Authorization.Calls == 2,
                "Revocation scenario did not cross the intended read boundary.");
        });

        run("RH-24 page-size boundaries and source overflow are enforced", () =>
        {
            foreach (var invalidSize in new[] { 0, -1, PlanRevisionHistoryService.MaximumPageSize + 1 })
            {
                var invalidContext = Context();
                var invalid = invalidContext.Service.ListAsync(new(
                    invalidContext.ProjectId, invalidContext.PlanId, invalidSize)).Result;
                Check(invalid.Status == PlanRevisionHistoryStatus.InvalidRequest &&
                    invalidContext.Port.TotalCalls == 0, "Invalid page size reached the source.");
            }

            var boundary = Context();
            var fifty = History(boundary.ProjectId, boundary.PlanId, 50);
            boundary.Port.PageResult = Page(fifty, false);
            var accepted = boundary.Service.ListAsync(new(
                boundary.ProjectId, boundary.PlanId, PlanRevisionHistoryService.MaximumPageSize)).Result;
            Check(accepted.Status == PlanRevisionHistoryStatus.Succeeded && accepted.Revisions?.Count == 50 &&
                boundary.Port.LastPageSize == 50, "Boundary page size was not forwarded and accepted exactly.");

            var overflow = Context();
            overflow.Port.PageResult = Page(History(overflow.ProjectId, overflow.PlanId, 51), false);
            var rejected = overflow.Service.ListAsync(new(overflow.ProjectId, overflow.PlanId, 50)).Result;
            CheckInvalid(rejected.Status, rejected.Revisions);
        });

        run("RH-25 empty continuation and unverified anchor fail closed", () =>
        {
            var context = Context();
            var history = History(context.ProjectId, context.PlanId, 2);
            var cursor = new PlanRevisionHistoryCursor(
                context.ProjectId, context.PlanId, 2, history[0].RevisionId,
                2, history[0].RevisionId, history[1].RevisionId);
            var head = new PlanRevisionHistoryHead(2, history[0].RevisionId);

            context.Port.PageResult = Page([], false, head, true);
            var empty = context.Service.ListAsync(new(
                context.ProjectId, context.PlanId, 10, cursor)).Result;
            CheckInvalid(empty.Status, empty.Revisions);

            context.Port.PageResult = Page([history[1]], false, head, false);
            var unattested = context.Service.ListAsync(new(
                context.ProjectId, context.PlanId, 10, cursor)).Result;
            CheckInvalid(unattested.Status, unattested.Revisions);
        });

        run("RH-26 premature terminal page is rejected", () =>
        {
            var context = Context();
            var history = History(context.ProjectId, context.PlanId, 2);
            context.Port.PageResult = Page([history[0]], false);

            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 1)).Result;

            CheckInvalid(result.Status, result.Revisions);
        });

        run("RH-27 cursor is bound to exact project and plan", () =>
        {
            foreach (var mismatch in new[] { "project", "plan" })
            {
                var context = Context();
                var cursor = new PlanRevisionHistoryCursor(
                    mismatch == "project" ? Guid.NewGuid() : context.ProjectId,
                    mismatch == "plan" ? Guid.NewGuid() : context.PlanId,
                    2, Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid());
                // Keep a same-version head/anchor structurally valid; scope alone must reject it.
                cursor = cursor with { HeadRevisionId = cursor.AfterRevisionId };
                var result = context.Service.ListAsync(new(
                    context.ProjectId, context.PlanId, 10, cursor)).Result;
                Check(result.Status == PlanRevisionHistoryStatus.InvalidCursor && context.Port.TotalCalls == 0,
                    "Cross-scope cursor reached authorization or the query port.");
            }
        });

        run("RH-28 anchor-only tampering is delegated to and rejected by trusted port", () =>
        {
            var context = Context();
            var history = History(context.ProjectId, context.PlanId, 3);
            context.Port.PageResult = Page([history[0], history[1]], true);
            var first = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 2)).Result;
            var original = first.NextCursor!.Value;
            context.Port.ExpectedCursor = original;

            var tampered = original with { AfterRevisionId = Guid.NewGuid() };
            context.Port.PageResult = Page([history[2]], false,
                new PlanRevisionHistoryHead(original.HeadVersionNumber, original.HeadRevisionId), true);
            var result = context.Service.ListAsync(new(
                context.ProjectId, context.PlanId, 1, tampered)).Result;

            Check(result.Status == PlanRevisionHistoryStatus.InvalidCursor && result.Revisions is null &&
                context.Port.LastCursor == tampered,
                "Trusted port did not receive and reject the altered stored-lineage anchor.");
        });

        run("RH-29 initial page requires authoritative matching head metadata", () =>
        {
            var context = Context();
            var history = History(context.ProjectId, context.PlanId, 2);
            foreach (var malformed in new[]
            {
                new PlanRevisionHistorySourcePage(PlanRevisionHistorySourceStatus.Succeeded, history, false),
                Page(history, false, new PlanRevisionHistoryHead(3, Guid.NewGuid())),
                new PlanRevisionHistorySourcePage(PlanRevisionHistorySourceStatus.Succeeded, [], false,
                    new PlanRevisionHistoryHead(1, Guid.NewGuid()))
            })
            {
                context.Port.PageResult = malformed;
                var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;
                CheckInvalid(result.Status, result.Revisions);
            }
        });

        run("RH-30 continuation remains on original head while fresh read sees concurrent append", () =>
        {
            var context = Context();
            var original = History(context.ProjectId, context.PlanId, 3);
            context.Port.PageResult = Page([original[0]], true);
            var first = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 1)).Result;

            var cursor = first.NextCursor!.Value;
            context.Port.ExpectedCursor = cursor;
            context.Port.PageResult = Page([original[1], original[2]], false,
                new PlanRevisionHistoryHead(cursor.HeadVersionNumber, cursor.HeadRevisionId), true);
            var continuation = context.Service.ListAsync(new(
                context.ProjectId, context.PlanId, 2, cursor)).Result;
            Check(continuation.Status == PlanRevisionHistoryStatus.Succeeded &&
                continuation.Revisions!.Select(item => item.VersionNumber).SequenceEqual([2L, 1L]),
                "Concurrent append disturbed the original traversal lineage.");

            var appended = Revision(context.ProjectId, context.PlanId, Guid.NewGuid(), 4, original[0].RevisionId);
            context.Port.ExpectedCursor = null;
            context.Port.PageResult = Page([appended, original[0], original[1], original[2]], false);
            var fresh = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 4)).Result;
            Check(fresh.Status == PlanRevisionHistoryStatus.Succeeded &&
                fresh.Revisions![0].RevisionId == appended.RevisionId,
                "Fresh traversal did not discover the new authoritative head.");
        });

        run("RH-31 duplicate and nonconsecutive history entries are rejected", () =>
        {
            var context = Context();
            var history = History(context.ProjectId, context.PlanId, 3).ToArray();
            var cases = new IReadOnlyList<PlanRevisionSnapshot>[]
            {
                [history[0], history[1] with { RevisionId = history[0].RevisionId }, history[2]],
                [history[0], history[1] with { VersionNumber = history[0].VersionNumber }, history[2]],
                [history[0] with { ParentRevisionId = history[2].RevisionId }, history[2]]
            };
            foreach (var malformed in cases)
            {
                context.Port.PageResult = Page(malformed, false);
                var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 3)).Result;
                CheckInvalid(result.Status, result.Revisions);
            }
        });

        run("RH-32 authorization-version change suppresses read payload", () =>
        {
            var context = Context();
            context.Port.PageResult = Page(History(context.ProjectId, context.PlanId, 1), false);
            context.Authorization.Results.Enqueue(Authorized(context.ProjectId, context.ActorId, 1));
            context.Authorization.Results.Enqueue(Authorized(context.ProjectId, context.ActorId, 2));

            var result = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;

            CheckDenied(result.Status, result.Revisions);
        });

        run("RH-33 malformed exact lookup and exact dependency failures are safe", () =>
        {
            foreach (var malformed in new PlanRevisionHistorySourceRevision?[]
            {
                null,
                new(PlanRevisionHistorySourceStatus.Succeeded),
                new((PlanRevisionHistorySourceStatus)999),
                new(PlanRevisionHistorySourceStatus.NotFound,
                    History(Guid.NewGuid(), Guid.NewGuid(), 1)[0])
            })
            {
                var context = Context();
                context.Port.RevisionResult = malformed;
                var result = context.Service.GetAsync(
                    context.ProjectId, context.PlanId, Guid.NewGuid()).Result;
                CheckInvalid(result.Status, result.Revision);
            }

            foreach (var failure in new Exception[]
                     { new TimeoutException("private"), new HttpRequestException("private") })
            {
                var context = Context();
                context.Port.RevisionFailure = failure;
                var result = context.Service.GetAsync(
                    context.ProjectId, context.PlanId, Guid.NewGuid()).Result;
                Check(result.Revision is null && result.Status ==
                    (failure is TimeoutException
                        ? PlanRevisionHistoryStatus.DependencyTimeout
                        : PlanRevisionHistoryStatus.DependencyUnavailable),
                    "Exact dependency failure escaped or exposed a snapshot.");
            }
        });

        run("RH-34 cancellation during read and final authorization propagates", () =>
        {
            using var readCancellation = new CancellationTokenSource();
            var readContext = Context();
            readContext.Port.OnPageRead = _ => readCancellation.Cancel();
            CheckThrows<OperationCanceledException>(() => readContext.Service.ListAsync(
                new(readContext.ProjectId, readContext.PlanId, 10), readCancellation.Token)
                .GetAwaiter().GetResult());

            using var authorizationCancellation = new CancellationTokenSource();
            var authorizationContext = Context();
            authorizationContext.Port.PageResult = Page(
                History(authorizationContext.ProjectId, authorizationContext.PlanId, 1), false);
            authorizationContext.Authorization.OnCall = (call, _) =>
            {
                if (call == 2) authorizationCancellation.Cancel();
            };
            CheckThrows<OperationCanceledException>(() => authorizationContext.Service.ListAsync(
                new(authorizationContext.ProjectId, authorizationContext.PlanId, 10),
                authorizationCancellation.Token).GetAwaiter().GetResult());
        });

        run("RH-35 maximum version invalid evidence time and duplicate JSON are rejected", () =>
        {
            var context = Context();
            var valid = History(context.ProjectId, context.PlanId, 1)[0];
            var duplicateJson = valid.GeometryJson.Insert(
                1, $"\"projectId\":\"{context.ProjectId}\",");
            var duplicateHash = PlanRevisionService.ComputeGeometryHash(duplicateJson);
            var invalid = new[]
            {
                valid with { VersionNumber = long.MaxValue, ParentRevisionId = Guid.NewGuid() },
                valid with { Validation = ValidationAt(valid.GeometryHash, default) },
                valid with
                {
                    GeometryJson = duplicateJson,
                    GeometryHash = duplicateHash,
                    Validation = Validation(duplicateHash)
                }
            };
            foreach (var revision in invalid)
            {
                context.Port.RevisionResult = new(PlanRevisionHistorySourceStatus.Succeeded, revision);
                var result = context.Service.GetAsync(
                    context.ProjectId, context.PlanId, revision.RevisionId).Result;
                CheckInvalid(result.Status, result.Revision);
            }
        });

        run("RH-36 result contracts prohibit protected failure payloads", () =>
        {
            var context = Context();
            var revision = History(context.ProjectId, context.PlanId, 1)[0];
            CheckThrows<ArgumentException>(() => new PlanRevisionHistoryPageResult(
                PlanRevisionHistoryStatus.DeniedOrNotFound, [revision]));
            CheckThrows<ArgumentException>(() => new PlanRevisionHistoryLookupResult(
                PlanRevisionHistoryStatus.InvalidDependencyResponse, revision));
            CheckThrows<ArgumentNullException>(() => new PlanRevisionHistoryPageResult(
                PlanRevisionHistoryStatus.Succeeded));
            CheckThrows<ArgumentNullException>(() => new PlanRevisionHistoryLookupResult(
                PlanRevisionHistoryStatus.Succeeded));
        });

        run("RH-23 history query has no mutation or confirmation dependency", () =>
        {
            var constructor = typeof(PlanRevisionHistoryService).GetConstructors().Single();
            var dependencies = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            Check(dependencies.SequenceEqual([
                typeof(ICurrentUser), typeof(IProjectAuthorizationPort), typeof(IPlanRevisionHistoryQueryPort)]),
                "History service acquired a write, confirmation, restore, or candidate dependency.");

            var context = Context();
            context.Port.PageResult = new(PlanRevisionHistorySourceStatus.Succeeded, [], false);
            _ = context.Service.ListAsync(new(context.ProjectId, context.PlanId, 10)).Result;
            Check(context.Port.PageCalls == 1 && context.Port.RevisionCalls == 0,
                "List query performed an unexpected operation.");
        });
    }

    private static ScenarioContext Context(bool authenticated = true)
    {
        var projectId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var authorization = new AuthorizationPortDouble(Authorized(projectId, actorId, 1));
        var port = new HistoryPortDouble();
        var service = new PlanRevisionHistoryService(
            new CurrentUserDouble(authenticated ? actorId : null), authorization, port);
        return new(projectId, planId, actorId, authorization, port, service);
    }

    private static PlanRevisionHistorySourcePage Page(
        IReadOnlyList<PlanRevisionSnapshot> revisions,
        bool hasMore,
        PlanRevisionHistoryHead? head = null,
        bool cursorAnchorVerified = false)
    {
        var effectiveHead = head ?? (revisions.Count == 0
            ? null
            : new PlanRevisionHistoryHead(revisions[0].VersionNumber, revisions[0].RevisionId));
        return new(PlanRevisionHistorySourceStatus.Succeeded, revisions, hasMore,
            effectiveHead, cursorAnchorVerified);
    }

    private static ProjectAuthorizationResult Authorized(Guid projectId, Guid actorId, long version) =>
        new(ProjectAuthorizationStatus.Authorized,
            new ProjectAuthorizationSnapshot(projectId, actorId, new AuthorizationVersion(version)));

    private static IReadOnlyList<PlanRevisionSnapshot> History(Guid projectId, Guid planId, int count)
    {
        var ascending = new List<PlanRevisionSnapshot>();
        Guid? parent = null;
        for (var version = 1; version <= count; version++)
        {
            var revision = Revision(projectId, planId, Guid.NewGuid(), version, parent);
            ascending.Add(revision);
            parent = revision.RevisionId;
        }
        ascending.Reverse();
        return ascending;
    }

    private static PlanRevisionSnapshot Revision(
        Guid projectId, Guid planId, Guid revisionId, long version, Guid? parent)
    {
        var geometry = Geometry(projectId, planId);
        var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        return new(revisionId, planId, projectId, parent, version, GeometrySchemaInfo.DraftVersion,
            json, hash, geometry.LandRevisionId, geometry.RequirementsRevisionId, Validation(hash),
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(version), $"revision {version}");
    }

    private static PlanGeometryDto Geometry(Guid projectId, Guid planId) => new()
    {
        SchemaVersion = GeometrySchemaInfo.DraftVersion,
        ProjectId = projectId,
        PlanId = planId,
        LandRevisionId = Guid.NewGuid(),
        RequirementsRevisionId = Guid.NewGuid(),
        CoordinateSystem = new CoordinateSystemDto
        {
            Type = "localCartesian", Unit = "m", Origin = new CoordinatePointDto { X = 0, Y = 0 },
            XDirection = "right", YDirection = "up"
        },
        Floors = [new FloorDto { Id = Guid.NewGuid(), Level = 0, Rooms = [], Walls = [], Openings = [] }]
    };

    private static PlanValidationReferenceDto Validation(
        string hash,
        DateTimeOffset? validatedAtUtc = null) => new()
    {
        ValidationId = Guid.NewGuid(),
        ValidatorVersion = "test-only/1",
        GeometryHash = hash,
        GeometryValid = true,
        RequirementsMatch = true,
        RegulatoryEvidenceStatus = "VERIFIED",
        HasBlockingIssues = false,
        ValidatedAtUtc = validatedAtUtc ?? DateTimeOffset.UtcNow
    };

    private static PlanValidationReferenceDto ValidationAt(string hash, DateTimeOffset validatedAtUtc) => new()
    {
        ValidationId = Guid.NewGuid(),
        ValidatorVersion = "test-only/1",
        GeometryHash = hash,
        GeometryValid = true,
        RequirementsMatch = true,
        RegulatoryEvidenceStatus = "VERIFIED",
        HasBlockingIssues = false,
        ValidatedAtUtc = validatedAtUtc
    };

    private static void CheckDenied(PlanRevisionHistoryStatus status, object? payload) =>
        Check(status == PlanRevisionHistoryStatus.DeniedOrNotFound && payload is null,
            "Denied query disclosed protected data or a distinguishable result.");

    private static void CheckInvalid(PlanRevisionHistoryStatus status, object? payload) =>
        Check(status == PlanRevisionHistoryStatus.InvalidDependencyResponse && payload is null,
            "Malformed dependency response disclosed protected data.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed record ScenarioContext(
        Guid ProjectId,
        Guid PlanId,
        Guid ActorId,
        AuthorizationPortDouble Authorization,
        HistoryPortDouble Port,
        PlanRevisionHistoryService Service);

    private sealed class CurrentUserDouble(Guid? actorId) : ICurrentUser
    {
        public bool IsAuthenticated => actorId is not null;
        public Guid? UserId => actorId;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public Guid GetRequiredUserId() => actorId ?? throw new UnauthenticatedAccessException();
    }

    private sealed class AuthorizationPortDouble(ProjectAuthorizationResult defaultResult)
        : IProjectAuthorizationPort
    {
        public Queue<ProjectAuthorizationResult> Results { get; } = new();
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Action<int, CancellationToken>? OnCall { get; set; }

        public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(
            Guid projectId, Guid authenticatedActorId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            OnCall?.Invoke(Calls, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) return Task.FromException<ProjectAuthorizationResult>(Failure);
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : defaultResult);
        }
    }

    private sealed class HistoryPortDouble : IPlanRevisionHistoryQueryPort
    {
        public PlanRevisionHistorySourcePage? PageResult { get; set; } =
            new(PlanRevisionHistorySourceStatus.Succeeded, [], false);
        public PlanRevisionHistorySourceRevision? RevisionResult { get; set; } =
            new(PlanRevisionHistorySourceStatus.NotFound);
        public Exception? PageFailure { get; set; }
        public Exception? RevisionFailure { get; set; }
        public PlanRevisionHistoryCursor? ExpectedCursor { get; set; }
        public Action<CancellationToken>? OnPageRead { get; set; }
        public int PageCalls { get; private set; }
        public int RevisionCalls { get; private set; }
        public int TotalCalls => PageCalls + RevisionCalls;
        public Guid LastProjectId { get; private set; }
        public Guid LastPlanId { get; private set; }
        public Guid LastRevisionId { get; private set; }
        public int LastPageSize { get; private set; }
        public PlanRevisionHistoryCursor? LastCursor { get; private set; }

        public Task<PlanRevisionHistorySourcePage> ReadPageAsync(
            Guid projectId, Guid planId, PlanRevisionHistoryCursor? cursor, int pageSize,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageCalls++;
            LastProjectId = projectId;
            LastPlanId = planId;
            LastCursor = cursor;
            LastPageSize = pageSize;
            OnPageRead?.Invoke(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (PageFailure is not null) return Task.FromException<PlanRevisionHistorySourcePage>(PageFailure);
            if (ExpectedCursor is { } expected && cursor != expected)
                return Task.FromResult(new PlanRevisionHistorySourcePage(
                    PlanRevisionHistorySourceStatus.InvalidCursor));
            return Task.FromResult(PageResult!);
        }

        public Task<PlanRevisionHistorySourceRevision> ReadRevisionAsync(
            Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RevisionCalls++;
            LastProjectId = projectId;
            LastPlanId = planId;
            LastRevisionId = revisionId;
            if (RevisionFailure is not null)
                return Task.FromException<PlanRevisionHistorySourceRevision>(RevisionFailure);
            return Task.FromResult(RevisionResult!);
        }
    }
}
