using System.Collections.Concurrent;

namespace BuildMind.API.Composition;

/// <summary>One background worker's observed state (no payloads, ids or exception messages: type names only).</summary>
public sealed record WorkerStatusDto(string Worker, DateTimeOffset? LastSuccessUtc, DateTimeOffset? LastFailureUtc,
    string? LastFailureType, long Successes, long Failures, bool Healthy);

public sealed record WorkerStatusListDto(DateTimeOffset ObservedAtUtc, IReadOnlyList<WorkerStatusDto> Items);

/// <summary>
/// In-process heartbeat registry updated by the hosted workers after every loop iteration. A worker is reported healthy
/// when it has succeeded and its last success is newer than its last failure and within <see cref="StaleAfter"/>.
/// Process-local by design: each API instance reports its own workers.
/// </summary>
public sealed class WorkerStatusRegistry(TimeProvider time)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public void Registered(string worker) => _entries.TryAdd(worker, new Entry());

    public void Succeeded(string worker)
    {
        var entry = _entries.GetOrAdd(worker, _ => new Entry());
        lock (entry) { entry.LastSuccess = time.GetUtcNow(); entry.Successes++; }
    }

    public void Failed(string worker, Exception exception)
    {
        var entry = _entries.GetOrAdd(worker, _ => new Entry());
        lock (entry) { entry.LastFailure = time.GetUtcNow(); entry.LastFailureType = exception.GetType().Name; entry.Failures++; }
    }

    public WorkerStatusListDto Snapshot()
    {
        var now = time.GetUtcNow();
        var items = _entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e =>
        {
            lock (e.Value)
            {
                var v = e.Value;
                var healthy = v.LastSuccess is { } s && (v.LastFailure is not { } f || s > f) && now - s <= StaleAfter;
                return new WorkerStatusDto(e.Key, v.LastSuccess, v.LastFailure, v.LastFailureType, v.Successes, v.Failures, healthy);
            }
        }).ToList();
        return new WorkerStatusListDto(now, items);
    }

    private sealed class Entry
    {
        public DateTimeOffset? LastSuccess;
        public DateTimeOffset? LastFailure;
        public string? LastFailureType;
        public long Successes;
        public long Failures;
    }
}
