namespace BuildMind.Application.Common.Validation;

public sealed record ValidationError(
    string Field,
    string Code,
    string Message);