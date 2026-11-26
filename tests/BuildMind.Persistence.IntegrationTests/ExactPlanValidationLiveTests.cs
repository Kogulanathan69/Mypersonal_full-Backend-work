using System.Text.Json;
using BuildMind.API;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using Microsoft.Extensions.Options;

namespace BuildMind.Persistence.IntegrationTests;

public sealed class ExactPlanValidationLiveTests
{
    private static PlanningComputeClient Client(HttpClient http)
    {
        var url = Environment.GetEnvironmentVariable(
            "BUILDMIND_TEST_EXACT_PYTHON_URL");

        var token = Environment.GetEnvironmentVariable(
            "BUILDMIND_TEST_EXACT_PYTHON_TOKEN");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.IsLoopback
            || uri.Scheme != "http")
        {
            throw new InvalidOperationException(
                "A loopback Python test URL is required.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "A Python test token is required.");
        }

        return new PlanningComputeClient(
            new PythonServiceClient(
                http,
                Options.Create(new ServicesOptions
                {
                    Python = new InternalServiceOptions
                    {
                        Enabled = true,
                        BaseUrl = url,
                        AuthToken = token,
                        TimeoutSeconds = 30
                    }
                }),
                new InternalServiceBulkhead()));
    }

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


    [Fact]
    public async Task Live_exact_geometry_preserves_original_hash()
    {
        using var http = new HttpClient(
            ApiServiceCollectionExtensions.CreateInternalServiceHandler());

        var request = Request();
        var raw = " \n" + request.GeometryJson + "\n ";

        request = request with
        {
            GeometryJson = raw,
            GeometryHash = PlanRevisionService.ComputeGeometryHash(raw)
        };

        var result = await Client(http)
            .ValidatePlanAsync(request, default);

        Assert.Equal("captain-plan-validation/2", result.ValidatorVersion);
        Assert.NotNull(result.Diagnostics);
    }

    [Fact]
    public async Task Live_maximum_geometry_payload_preserves_hash()
    {
        using var http = new HttpClient(
            ApiServiceCollectionExtensions.CreateInternalServiceHandler());

        const int limit = 1024 * 1024;

        var request = Request();
        var size = System.Text.Encoding.UTF8.GetByteCount(
            request.GeometryJson);

        Assert.InRange(size, 1, limit);

        var raw = request.GeometryJson
            + new string('\t', limit - size);

        Assert.Equal(limit,
            System.Text.Encoding.UTF8.GetByteCount(raw));

        request = request with
        {
            GeometryJson = raw,
            GeometryHash = PlanRevisionService.ComputeGeometryHash(raw)
        };

        var result = await Client(http)
            .ValidatePlanAsync(request, default);

        Assert.Equal("captain-plan-validation/2", result.ValidatorVersion);
        Assert.NotNull(result.Diagnostics);
    }

    [Fact]
    public async Task Live_realistic_geometry_matches_requirements()
    {
        using var http = new HttpClient(
            ApiServiceCollectionExtensions.CreateInternalServiceHandler());

        var fixture = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "bm-int-01-synthetic-geometry.json");

        Assert.True(File.Exists(fixture));

        var raw = await File.ReadAllTextAsync(fixture);
        var hash = PlanRevisionService.ComputeGeometryHash(raw);

        Assert.Equal(
            "sha256:e3d53b41d349923b43490c9bb2f0960f01aead4f1fcfeb3c52cc7e3883100332",
            hash);

        var geometry = JsonSerializer.Deserialize<PlanGeometryDto>(
            raw, PlanEditorJson.StrictOptions);

        Assert.NotNull(geometry);

        var floor = Assert.Single(geometry.Floors);

        Assert.Equal(4, floor.Rooms.Count());
        Assert.Equal(20, floor.Walls.Count());
        Assert.Equal(8, floor.Openings.Count());

        var baseline = Request();

        var request = new PlanCheckRequest(
            geometry.ProjectId,
            geometry.LandRevisionId,
            baseline.Land,
            new double[] { 1, 1, 1, 1 },
            new Dictionary<string, int>
            {
                ["bedroom"] = 1,
                ["livingRoom"] = 1,
                ["kitchen"] = 1,
                ["bathroom"] = 1
            },
            geometry,
            raw,
            hash);

        var result = await Client(http)
            .ValidatePlanAsync(request, default);

        Assert.Equal(
            "captain-plan-validation/2",
            result.ValidatorVersion);

        Assert.True(result.GeometryValid);
        Assert.True(result.RequirementsMatch == true);
        Assert.Empty(result.Diagnostics);
    }

}
