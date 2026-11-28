using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.Land.Contracts;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Integration.Land;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Integration;

public sealed class M2LandGeometryValidatorTests
{
    private const string ValidResponse = """
    {
      "validation_status": "VALID",
      "source_crs": "EPSG:4326",
      "calculation_crs": "EPSG:32644",
      "geometry_crs": "EPSG:4326",
      "measurements": {"area_m2": 100, "perimeter_m": 40},
      "normalized_metric_exterior": [[0,0],[10,0],[10,10],[0,10],[0,0]],
      "normalized_metric_holes": [],
      "geojson": null,
      "diagnostic_code": null,
      "legal_boundary_verified": false
    }
    """;

    private static LandGeometryValidationRequest Request() =>
        new("EPSG:4326", "EPSG:32644",
            [[79.8,9.6],[79.81,9.6],[79.81,9.61],[79.8,9.61],[79.8,9.6]], []);

    private static PythonLandGeometryValidator Validator(string json) =>
        new(new PythonServiceClient(
            new HttpClient(StubHttpHandler.Json(json)),
            Options.Create(new ServicesOptions
            {
                Python = new InternalServiceOptions
                {
                    Enabled = true,
                    BaseUrl = "http://python-services.internal:8000/",
                    TimeoutSeconds = 5
                }
            }),
            new InternalServiceBulkhead()));

    [Fact]
    public async Task Valid_response_is_accepted()
    {
        var result = await Validator(ValidResponse)
            .ValidateAsync(Request(), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(100d, result.Measurements!.AreaM2);
    }

    [Theory]
    [InlineData("EPSG:32644", "EPSG:32643")]
    [InlineData("\"legal_boundary_verified\": false", "\"legal_boundary_verified\": true")]
    [InlineData("\"legal_boundary_verified\": false", "\"legal_boundary_verified\": null")]
    [InlineData("[0,10],[0,0]", "[0,10],[1,1]")]
    [InlineData("\"normalized_metric_holes\": []", "\"normalized_metric_holes\": null")]
    [InlineData("\"area_m2\": 100", "\"area_m2\": -100")]
    [InlineData("\"perimeter_m\": 40", "\"perimeter_m\": 0")]
    public async Task Malformed_response_is_rejected(string oldValue, string newValue)
    {
        var json = ValidResponse.Replace(oldValue, newValue);

        var error = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Validator(json).ValidateAsync(Request(), CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, error.Kind);
    }

    [Fact]
    public async Task Missing_legal_flag_is_rejected()
    {
        var json = ValidResponse.Replace(
            ",\n  \"legal_boundary_verified\": false", "");

        Assert.DoesNotContain("legal_boundary_verified", json);

        await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Validator(json).ValidateAsync(Request(), CancellationToken.None));
    }

    [Theory]
    [InlineData("4326", "32644")]
    [InlineData("epsg:4326", "epsg:32644")]
    [InlineData("urn:ogc:def:crs:EPSG::4326", "urn:ogc:def:crs:EPSG::32644")]
    [InlineData("WGS84", "EPSG::32644")]
    public async Task Canonical_crs_aliases_match_original_request_crs(string sourceCrs, string calculationCrs)
    {
        var request = new LandGeometryValidationRequest(sourceCrs, calculationCrs,
            [[79.8, 9.6], [79.81, 9.6], [79.81, 9.61], [79.8, 9.61], [79.8, 9.6]], []);

        var result = await Validator(ValidResponse)
            .ValidateAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("EPSG:4326", result.SourceCrs);
        Assert.Equal("EPSG:32644", result.CalculationCrs);
    }

    [Theory]
    [InlineData("4326", "32643")]
    [InlineData("EPSG:3857", "EPSG:32644")]
    public async Task Mismatched_crs_aliases_are_still_rejected(string sourceCrs, string calculationCrs)
    {
        var request = new LandGeometryValidationRequest(sourceCrs, calculationCrs,
            [[79.8, 9.6], [79.81, 9.6], [79.81, 9.61], [79.8, 9.61], [79.8, 9.6]], []);

        var error = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Validator(ValidResponse).ValidateAsync(request, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, error.Kind);
    }
}