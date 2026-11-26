using System.Reflection;
using System.Text.RegularExpressions;
using BuildMind.Application.Abstractions.Errors;

namespace BuildMind.Infrastructure.Tests.Errors;

public sealed class SharedErrorCodesTests
{
    private static readonly string[] ApprovedCodes =
    [
        "MALFORMED_REQUEST", "VALIDATION_FAILED", "UNAUTHENTICATED", "FORBIDDEN", "NOT_FOUND", "CONFLICT",
        "REVISION_CONFLICT", "DUPLICATE_REQUEST", "PAYLOAD_TOO_LARGE", "UNSUPPORTED_MEDIA_TYPE",
        "RATE_LIMITED", "DEPENDENCY_UNAVAILABLE", "DEPENDENCY_TIMEOUT", "INTERNAL_ERROR",
    ];

    [Fact]
    public void Exactly_the_approved_shared_codes_are_defined()
    {
        Assert.Equal(
            ApprovedCodes.OrderBy(c => c, StringComparer.Ordinal),
            SharedErrorCodes.All.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void Codes_are_unique()
    {
        Assert.Equal(SharedErrorCodes.All.Count, SharedErrorCodes.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Codes_follow_upper_snake_case()
    {
        var pattern = new Regex("^[A-Z]+(_[A-Z]+)*$");
        Assert.All(SharedErrorCodes.All, code => Assert.Matches(pattern, code));
    }

    [Fact]
    public void Every_constant_is_listed_once_in_All()
    {
        var constants = typeof(SharedErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(c => c, StringComparer.Ordinal);

        Assert.Equal(constants, SharedErrorCodes.All.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(SharedErrorCodes.MalformedRequest, "MALFORMED_REQUEST")]
    [InlineData(SharedErrorCodes.ValidationFailed, "VALIDATION_FAILED")]
    [InlineData(SharedErrorCodes.Unauthenticated, "UNAUTHENTICATED")]
    [InlineData(SharedErrorCodes.Forbidden, "FORBIDDEN")]
    [InlineData(SharedErrorCodes.NotFound, "NOT_FOUND")]
    [InlineData(SharedErrorCodes.Conflict, "CONFLICT")]
    [InlineData(SharedErrorCodes.RevisionConflict, "REVISION_CONFLICT")]
    [InlineData(SharedErrorCodes.DuplicateRequest, "DUPLICATE_REQUEST")]
    [InlineData(SharedErrorCodes.PayloadTooLarge, "PAYLOAD_TOO_LARGE")]
    [InlineData(SharedErrorCodes.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE")]
    [InlineData(SharedErrorCodes.RateLimited, "RATE_LIMITED")]
    [InlineData(SharedErrorCodes.DependencyUnavailable, "DEPENDENCY_UNAVAILABLE")]
    [InlineData(SharedErrorCodes.DependencyTimeout, "DEPENDENCY_TIMEOUT")]
    [InlineData(SharedErrorCodes.InternalError, "INTERNAL_ERROR")]
    public void Code_values_are_stable(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }
}
