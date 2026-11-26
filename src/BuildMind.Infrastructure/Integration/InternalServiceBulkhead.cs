using System.Collections.Concurrent;

namespace BuildMind.Infrastructure.Integration;

/// <summary>
/// Caps the number of in-flight PLUS queued calls per internal dependency (STEP 08-C). The transport's
/// <c>MaxConnectionsPerServer</c> only bounds connections; requests beyond it wait in an unbounded queue, so without
/// this a slow dependency could accumulate one waiting request per concurrent API caller. A call that finds no free
/// slot fails immediately (503 <c>DEPENDENCY_UNAVAILABLE</c>) without touching the network. Register as a singleton.
/// </summary>
public sealed class InternalServiceBulkhead(int maxConcurrentCallsPerDependency = InternalServiceBulkhead.DefaultMaxConcurrentCalls)
{
    /// <summary>Twice the transport's 32 connections: a bounded queue in front of the connection pool.</summary>
    public const int DefaultMaxConcurrentCalls = 64;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> slots = new(StringComparer.Ordinal);

    public int MaxConcurrentCallsPerDependency { get; } = maxConcurrentCallsPerDependency > 0
        ? maxConcurrentCallsPerDependency
        : throw new ArgumentOutOfRangeException(nameof(maxConcurrentCallsPerDependency));

    /// <summary>Takes a slot without waiting; returns null when the dependency is saturated.</summary>
    internal IDisposable? TryEnter(string dependency)
    {
        var semaphore = slots.GetOrAdd(dependency, _ => new SemaphoreSlim(MaxConcurrentCallsPerDependency, MaxConcurrentCallsPerDependency));
        return semaphore.Wait(0) ? new Slot(semaphore) : null;
    }

    internal int Available(string dependency) =>
        slots.TryGetValue(dependency, out var semaphore) ? semaphore.CurrentCount : MaxConcurrentCallsPerDependency;

    private sealed class Slot(SemaphoreSlim semaphore) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
