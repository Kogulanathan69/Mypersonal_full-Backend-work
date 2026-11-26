using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Support;

/// <summary>Finds a specific exception inside wrapped/aggregate host start-up failures.</summary>
public static class ExceptionChains
{
    /// <summary>Log category under which the generic host records its own start-up failure.</summary>
    public const string HostCategory = "Microsoft.Extensions.Hosting.Internal.Host";

    public static OptionsValidationException RequireOptionsValidationException(Exception? exception)
    {
        Assert.NotNull(exception);
        if (TryFind(exception!, out var validation))
        {
            return validation;
        }

        throw new Xunit.Sdk.XunitException(
            $"Expected an OptionsValidationException but got {exception!.GetType().FullName}.");
    }

    /// <summary>
    /// Requires that host start-up failed because of options validation. With minimal hosting,
    /// <c>WebApplicationFactory</c> waits for start-up through the application's service provider; when start-up
    /// fails, the application disposes that provider and the factory can observe an
    /// <see cref="ObjectDisposedException"/> instead of the original failure (a timing race). That exception is
    /// never accepted on its own: it is tolerated only when the host itself recorded an
    /// <see cref="OptionsValidationException"/> as its start-up failure, and that recorded exception is returned.
    /// </summary>
    public static OptionsValidationException RequireStartupOptionsValidationFailure(
        Exception? thrown,
        CapturingLoggerProvider logs)
    {
        Assert.NotNull(thrown);
        if (TryFind(thrown!, out var direct))
        {
            return direct;
        }

        Assert.IsType<ObjectDisposedException>(thrown);
        return RequireHostStartupFailure(logs);
    }

    /// <summary>Returns the <see cref="OptionsValidationException"/> the host logged as its start-up failure.</summary>
    public static OptionsValidationException RequireHostStartupFailure(CapturingLoggerProvider logs)
    {
        foreach (var entry in logs.Entries)
        {
            if (entry.Category == HostCategory
                && entry.Level >= LogLevel.Error
                && entry.ExceptionObject is not null
                && TryFind(entry.ExceptionObject, out var recorded))
            {
                return recorded;
            }
        }

        throw new Xunit.Sdk.XunitException(
            "Expected the host to record an OptionsValidationException as its start-up failure, but none was captured.");
    }

    private static bool TryFind(Exception exception, out OptionsValidationException validation)
    {
        var pending = new Queue<Exception>([exception]);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (current is OptionsValidationException found)
            {
                validation = found;
                return true;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Enqueue(inner);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Enqueue(current.InnerException);
            }
        }

        validation = null!;
        return false;
    }
}
