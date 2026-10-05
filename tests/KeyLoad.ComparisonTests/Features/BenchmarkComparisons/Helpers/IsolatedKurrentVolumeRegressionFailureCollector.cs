using System.Globalization;
using System.Runtime.ExceptionServices;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKurrentVolumeRegressionFailureCollector
{
    private readonly System.Threading.Lock gate = new();
    private ExceptionDispatchInfo? first;
    private ExceptionDispatchInfo? fatal;
    private int cleanupFailureCount;

    internal async Task AttemptCleanupAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception error) when (!IsFatalException(error))
        {
            CaptureCleanup(error);
        }
        catch (Exception error) when (IsFatalException(error))
        {
            CaptureCleanup(error);
        }
    }

    internal void Capture(Exception error) => CaptureCore(error, cleanup: false);
    internal void CaptureCleanup(Exception error) => CaptureCore(error, cleanup: true);

    internal void ThrowAfterCleanup(ExceptionDispatchInfo? primary)
    {
        WriteCleanupFailureCount();
        FatalCause(primary?.SourceException)?.Throw();
        ExceptionDispatchInfo? cleanup;
        ExceptionDispatchInfo? fatalCleanup;
        lock (gate)
        {
            cleanup = first;
            fatalCleanup = fatal;
        }
        fatalCleanup?.Throw();
        primary?.Throw();
        cleanup?.Throw();
    }

    internal void ThrowAfterWorkers()
    {
        ExceptionDispatchInfo? failure;
        lock (gate)
        {
            failure = fatal ?? first;
        }
        failure?.Throw();
    }

    internal static bool IsFatalException(Exception error) => FatalCause(error) is not null;

    private void CaptureCore(Exception error, bool cleanup)
    {
        if (cleanup)
        {
            Interlocked.Increment(ref cleanupFailureCount);
        }
        var captured = ExceptionDispatchInfo.Capture(error);
        var fatalError = FatalCause(error);
        lock (gate)
        {
            if (fatalError is not null)
            {
                fatal ??= fatalError;
            }
            else
            {
                first ??= captured;
            }
        }
    }

    private void WriteCleanupFailureCount()
    {
        var count = Volatile.Read(ref cleanupFailureCount);
        if (count == 0)
        {
            return;
        }
        try
        {
            Console.Error.WriteLine("KurrentVolumeCleanupFailureCount=" + count.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        {
            Capture(error);
        }
    }

    private static ExceptionDispatchInfo? FatalCause(Exception? error)
    {
        if (error is null)
        {
            return null;
        }
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>();
        pending.Push(error);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }
            if (IsFatalType(current))
            {
                return ExceptionDispatchInfo.Capture(current);
            }
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push(inner);
                }
            }
            if (current.InnerException is { } innerException)
            {
                pending.Push(innerException);
            }
        }
        return null;
    }

    private static bool IsFatalType(Exception error)
        => error is OutOfMemoryException or StackOverflowException or AccessViolationException
            or AppDomainUnloadedException or BadImageFormatException or CannotUnloadAppDomainException;
}
