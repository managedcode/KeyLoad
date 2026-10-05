namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Roots the one scaled fixture and serializes its actual close attempts.</summary>
internal static class ScaledRawStorageOwnership
{
    private static readonly System.Threading.Lock Gate = new();
    private static ScaledRawStorageFixture? _fixture;
    private static bool _failedConstruction;
    private static bool _closeInProgress;
    private const string ActiveMessage = "A scaled native fixture already owns this diagnostic process.";
    private const string OwnershipMessage = "The scaled fixture does not own the process slot.";

    internal static bool HasRetainedFailedOwner
    {
        get
        {
            lock (Gate)
            {
                return _fixture is not null && _failedConstruction;
            }
        }
    }

    internal static void Admit(ScaledRawStorageFixture fixture)
    {
        lock (Gate)
        {
            if (_fixture is not null)
            {
                throw new InvalidOperationException(ActiveMessage);
            }

            _fixture = fixture;
            _failedConstruction = false;
            _closeInProgress = false;
        }
    }

    internal static void ReleaseUninitialized(ScaledRawStorageFixture fixture)
    {
        lock (Gate)
        {
            EnsureOwner(fixture);
            if (_closeInProgress)
            {
                throw new InvalidOperationException(OwnershipMessage);
            }

            ClearOwner();
        }
    }

    internal static void CloseHealthy(ScaledRawStorageFixture fixture)
    {
        lock (Gate)
        {
            EnsureOwner(fixture);
            if (_failedConstruction || _closeInProgress)
            {
                throw new InvalidOperationException(OwnershipMessage);
            }

            fixture.MarkClosing();
            _closeInProgress = true;
        }

        try
        {
            fixture.CloseActiveResources();
        }
        finally
        {
            FinishAttempt(fixture);
        }
    }

    internal static void CloseFailedConstruction(ScaledRawStorageFixture fixture)
    {
        lock (Gate)
        {
            EnsureOwner(fixture);
            if (_closeInProgress)
            {
                throw new InvalidOperationException(OwnershipMessage);
            }

            _failedConstruction = true;
            fixture.MarkClosing();
            _closeInProgress = true;
        }

        var returned = false;
        try
        {
            fixture.CloseFailedConstructionResources();
            returned = true;
        }
        finally
        {
            FinishAttempt(fixture, returned);
        }
    }

    internal static bool RetryFailedOwnerClose()
    {
        ScaledRawStorageFixture fixture;
        lock (Gate)
        {
            var current = _fixture;
            if (current is null || !_failedConstruction || _closeInProgress)
            {
                return false;
            }

            fixture = current;
            fixture.MarkClosing();
            _closeInProgress = true;
        }

        bool closed;
        var returned = false;
        try
        {
            fixture.CloseFailedConstructionResources();
            returned = true;
        }
        finally
        {
            closed = FinishAttempt(fixture, returned);
        }

        return returned && closed;
    }

    private static bool FinishAttempt(ScaledRawStorageFixture fixture, bool releaseIfClosed = true)
    {
        lock (Gate)
        {
            EnsureOwner(fixture);
            var closed = fixture.IsClosed;
            if (closed && releaseIfClosed)
            {
                ClearOwner();
            }
            else
            {
                _closeInProgress = false;
            }

            return closed;
        }
    }

    private static void EnsureOwner(ScaledRawStorageFixture fixture)
    {
        if (!ReferenceEquals(_fixture, fixture))
        {
            throw new InvalidOperationException(OwnershipMessage);
        }
    }

    private static void ClearOwner()
    {
        _fixture = null;
        _failedConstruction = false;
        _closeInProgress = false;
    }
}
