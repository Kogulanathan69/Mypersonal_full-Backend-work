using System.Text.Json;
using System.Text.Json.Nodes;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Integration;

public sealed class ExactPlanValidationTests
{
    private const string Schema = "captain-plan-validation/2";

    private static PlanningComputeClient Client(StubHttpHandler handler) =>
        new(new PythonServiceClient(
            new HttpClient(handler),
            Options.Create(new ServicesOptions
            {
                Python = new InternalServiceOptions
                {
                    Enabled = true,
                    BaseUrl = "https://python.internal:8000",
                    TimeoutSeconds = 5
                }
            }),
            new InternalServiceBulkhead()));

    private static PlanCheckRequest Request()
    {
        var project = Guid.NewGuid();
        var land = Guid.NewGuid();

        var coordinateSystem = new CoordinateSystemDto
        {
            Type = "localCartesian",
            Unit = "m",
            Origin = new() { X = 0, Y = 0 },
            XDirection = "right",
            YDirection = "up"
        };

        var geometry = new PlanGeometryDto
        {
            SchemaVersion = GeometrySchemaInfo.DraftVersion,
            ProjectId = project,
            PlanId = Guid.NewGuid(),
            LandRevisionId = land,
            RequirementsRevisionId = Guid.NewGuid(),
            CoordinateSystem = coordinateSystem,
            Floors =
            [
                new FloorDto
                {
                    Id = Guid.NewGuid(),
                    Level = 0,
                    Rooms = [],
                    Walls = [],
                    Openings = []
                }
            ]
        };

        var metricLand = new MetricLandDto
        {
            SchemaVersion = PlanningSchemas.MetricLand,
            CoordinateSystem = coordinateSystem,
            Boundary = new PolygonBoundaryDto
            {
                Exterior =
                [
                    new() { X = 0, Y = 0 },
                    new() { X = 12, Y = 0 },
                    new() { X = 12, Y = 10 },
                    new() { X = 0, Y = 10 },
                    new() { X = 0, Y = 0 }
                ]
            }
        };

        var json = JsonSerializer.Serialize(
            geometry, PlanEditorJson.StrictOptions);

        return new PlanCheckRequest(
            project, land, metricLand, null, null,
            geometry, json,
            PlanRevisionService.ComputeGeometryHash(json));
    }

    private static JsonObject Response(string hash) => new()
    {
        ["schemaVersion"] = Schema,
        ["validatorVersion"] = Schema,
        ["geometryValid"] = true,
        ["requirementsMatch"] = true,
        ["geometryHash"] = hash,
        ["diagnostics"] = new JsonArray()
    };

    [Fact]
    public async Task Exact_json_is_transmitted_without_rewriting()
    {
        var request = Request();
        var raw = " \n" + request.GeometryJson + "\n ";

        request = request with
        {
            GeometryJson = raw,
            GeometryHash = PlanRevisionService.ComputeGeometryHash(raw)
        };

        var handler = StubHttpHandler.Json(
            Response(request.GeometryHash).ToJsonString());

        var result = await Client(handler)
            .ValidatePlanAsync(request, default);

        Assert.True(result.GeometryValid);
        Assert.Equal(1, handler.Calls);

        Assert.Equal(
            "/internal/v2/plans/validate",
            handler.Requests.Single().RequestUri!.AbsolutePath);

        using var body = JsonDocument.Parse(
            handler.RequestBodies.Single()!);

        Assert.Equal(raw,
            body.RootElement.GetProperty("geometryJson").GetString());

        Assert.Equal(request.GeometryHash,
            body.RootElement.GetProperty("geometryHash").GetString());

        Assert.False(
            body.RootElement.TryGetProperty("geometry", out _));
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("validatorVersion")]
    [InlineData("geometryValid")]
    [InlineData("requirementsMatch")]
    [InlineData("geometryHash")]
    [InlineData("diagnostics")]
    public async Task Missing_response_field_is_rejected(string field)
    {
        var request = Request();
        var response = Response(request.GeometryHash);
        response.Remove(field);

        var handler = StubHttpHandler.Json(response.ToJsonString());

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(
            () => Client(handler).ValidatePlanAsync(request, default));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Wrong_response_hash_is_rejected()
    {
        var request = Request();
        var handler = StubHttpHandler.Json(
            Response("sha256:" + new string('0', 64)).ToJsonString());

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(
            () => Client(handler).ValidatePlanAsync(request, default));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Wrong_request_hash_is_rejected_without_network()
    {
        var request = Request() with
        {
            GeometryHash = "sha256:" + new string('0', 64)
        };

        var handler = StubHttpHandler.Json("{}");

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(
            () => Client(handler).ValidatePlanAsync(request, default));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Different_geometry_with_matching_hash_is_rejected()
    {
        var request = Request();
        var changed = JsonNode.Parse(request.GeometryJson)!.AsObject();
        changed["planId"] = Guid.NewGuid().ToString();

        var raw = changed.ToJsonString();

        request = request with
        {
            GeometryJson = raw,
            GeometryHash = PlanRevisionService.ComputeGeometryHash(raw)
        };

        var handler = StubHttpHandler.Json("{}");

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(
            () => Client(handler).ValidatePlanAsync(request, default));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Legacy_validator_version_is_rejected()
    {
        var request = Request();
        var response = Response(request.GeometryHash);
        response["validatorVersion"] = "captain-plan-validation/1";

        var handler = StubHttpHandler.Json(response.ToJsonString());

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(
            () => Client(handler).ValidatePlanAsync(request, default));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Fact]
    public async Task Unknown_diagnostic_severity_is_rejected()
    {
        var request = Request();
        var response = Response(request.GeometryHash);

        response["diagnostics"] = new JsonArray(new JsonObject
        {
            ["code"] = "SYNTHETIC_TEST",
            ["severity"] = "UNKNOWN",
            ["message"] = "Synthetic test diagnostic."
        });

        var handler = StubHttpHandler.Json(response.ToJsonString());

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(
            () => Client(handler).ValidatePlanAsync(request, default));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Theory]
    [InlineData("null-entry")]
    [InlineData("blank-code")]
    [InlineData("null-code")]
    [InlineData("blank-message")]
    [InlineData("null-message")]
    [InlineData("null-severity")]
    [InlineData("lowercase-severity")]
    [InlineData("null-related-entities")]
    [InlineData("null-source-references")]
    [InlineData("blank-source-reference")]
    [InlineData("null-source-reference")]
    [InlineData("critical-with-valid-geometry")]
    public async Task Malformed_diagnostic_is_rejected(string scenario)
    {
        var request = Request();
        var response = Response(request.GeometryHash);

        var diagnostic = new JsonObject
        {
            ["code"] = "SYNTHETIC_TEST",
            ["severity"] = "WARNING",
            ["message"] = "Synthetic diagnostic."
        };

        switch (scenario)
        {
            case "blank-code":
                diagnostic["code"] = " ";
                break;
            case "null-code":
                diagnostic["code"] = null;
                break;
            case "blank-message":
                diagnostic["message"] = " ";
                break;
            case "null-message":
                diagnostic["message"] = null;
                break;
            case "null-severity":
                diagnostic["severity"] = null;
                break;
            case "lowercase-severity":
                diagnostic["severity"] = "warning";
                break;
            case "null-related-entities":
                diagnostic["relatedEntityIds"] = null;
                break;
            case "null-source-references":
                diagnostic["sourceReferenceIds"] = null;
                break;
            case "blank-source-reference":
                diagnostic["sourceReferenceIds"] = new JsonArray(
                    JsonValue.Create(" "));
                break;
            case "null-source-reference":
                diagnostic["sourceReferenceIds"] = new JsonArray(
                    new JsonNode?[] { null });
                break;
            case "critical-with-valid-geometry":
                diagnostic["severity"] = "CRITICAL";
                break;
        }

        response["diagnostics"] = scenario == "null-entry"
            ? new JsonArray(new JsonNode?[] { null })
            : new JsonArray(diagnostic);

        var handler = StubHttpHandler.Json(response.ToJsonString());

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(
            () => Client(handler).ValidatePlanAsync(request, default));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Requirements_mismatch_warning_preserves_geometry_validity()
    {
        var request = Request();
        var response = Response(request.GeometryHash);

        response["requirementsMatch"] = false;
        response["diagnostics"] = new JsonArray(new JsonObject
        {
            ["code"] = "ROOM_COUNTS_MISMATCH",
            ["severity"] = "WARNING",
            ["message"] = "Room counts differ from requirements."
        });

        var handler = StubHttpHandler.Json(response.ToJsonString());
        var result = await Client(handler)
            .ValidatePlanAsync(request, default);

        Assert.True(result.GeometryValid);
        Assert.Equal(false, result.RequirementsMatch);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public async Task Invalid_geometry_with_critical_diagnostic_is_preserved()
    {
        var request = Request();
        var response = Response(request.GeometryHash);

        response["geometryValid"] = false;
        response["diagnostics"] = new JsonArray(new JsonObject
        {
            ["code"] = "ROOMS_OVERLAP",
            ["severity"] = "CRITICAL",
            ["message"] = "Two rooms overlap."
        });

        var handler = StubHttpHandler.Json(response.ToJsonString());
        var result = await Client(handler)
            .ValidatePlanAsync(request, default);

        Assert.False(result.GeometryValid);
        Assert.Single(result.Diagnostics);
    }



    [Fact]
    public async Task Maximum_geometry_survives_actual_http_serialization()
    {
        const int geometryLimit = 1024 * 1024;
        const int outerLimit = 8 * 1024 * 1024;

        var request = Request();

        var raw = request.GeometryJson + new string(
            '\t',
            geometryLimit -
            System.Text.Encoding.UTF8.GetByteCount(request.GeometryJson));

        Assert.Equal(
            geometryLimit,
            System.Text.Encoding.UTF8.GetByteCount(raw));

        request = request with
        {
            GeometryJson = raw,
            GeometryHash = PlanRevisionService.ComputeGeometryHash(raw)
        };

        var handler = StubHttpHandler.Json(
            Response(request.GeometryHash).ToJsonString());

        var result = await Client(handler)
            .ValidatePlanAsync(request, default);

        Assert.True(result.GeometryValid);
        Assert.Equal(1, handler.Calls);

        var transmitted = handler.RequestBodies.Single()!;
        var transmittedBytes =
            System.Text.Encoding.UTF8.GetByteCount(transmitted);

        Assert.InRange(
            transmittedBytes,
            geometryLimit + 1,
            outerLimit);

        using var body = JsonDocument.Parse(transmitted);

        Assert.Equal(
            raw,
            body.RootElement.GetProperty("geometryJson").GetString());

        Assert.Equal(
            request.GeometryHash,
            body.RootElement.GetProperty("geometryHash").GetString());

        Assert.False(
            body.RootElement.TryGetProperty("geometry", out _));
    }

    [Fact]
    public async Task Oversized_geometry_is_rejected_without_network()
    {
        const int geometryLimit = 1024 * 1024;

        var request = Request();

        var raw = request.GeometryJson + new string(
            ' ',
            geometryLimit + 1 -
            System.Text.Encoding.UTF8.GetByteCount(request.GeometryJson));

        request = request with
        {
            GeometryJson = raw,
            GeometryHash = PlanRevisionService.ComputeGeometryHash(raw)
        };

        var handler = StubHttpHandler.Json("{}");

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(
            () => Client(handler).ValidatePlanAsync(request, default));

        Assert.Equal(
            DependencyFailureKind.MalformedResponse,
            failure.Kind);

        Assert.Equal(0, handler.Calls);
    }
}
