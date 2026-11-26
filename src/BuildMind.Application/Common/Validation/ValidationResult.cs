namespace BuildMind.Application.Common.Validation;

public sealed class ValidationResult
{
    private readonly List<ValidationError> _errors = new();

    public bool IsValid => _errors.Count == 0;

    public IReadOnlyList<ValidationError> Errors
        => _errors.AsReadOnly();

    public void AddError(
        string field,
        string code,
        string message)
    {
        _errors.Add(
            new ValidationError(field, code, message));
    }
}