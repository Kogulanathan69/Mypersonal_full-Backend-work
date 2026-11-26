using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BuildMind.Infrastructure.Tests.Support;

/// <summary>Test-only logger provider that records every log entry the host emits.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyCollection<CapturedLogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            entries.Enqueue(new CapturedLogEntry(
                category,
                logLevel,
                formatter(state, exception),
                exception?.ToString(),
                properties.ToDictionary(p => p.Key, p => p.Value?.ToString()))
            {
                ExceptionObject = exception,
            });
        }
    }
}

public sealed record CapturedLogEntry(
    string Category,
    LogLevel Level,
    string Message,
    string? Exception,
    IReadOnlyDictionary<string, string?> Properties)
{
    /// <summary>The logged exception instance, kept so tests can inspect its type chain.</summary>
    public Exception? ExceptionObject { get; init; }

    public string AllText => $"{Category} {Message} {Exception} {string.Join(' ', Properties.Values)}";
}
