using System.Runtime.ExceptionServices;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

/// <summary>Tracks a verified capability phase and joins its exact silo-local work lease.</summary>
internal sealed class NativeCapabilityWorkLifetime : IDisposable
{
    private const int NoPhaseStarted = -1;

    private readonly CancellationToken requestToken;
    private NativeRequestWorkLease? lease;
    private CancellationTokenSource? linkedCancellation;
    private bool settled;

    internal NativeCapabilityWorkLifetime(CancellationToken requestToken) => this.requestToken = requestToken;

    internal GrainFailureStage Stage { get; set; } = GrainFailureStage.EnvelopeVerification;
    internal Guid RequestId { get; set; }
    internal DatabasePhaseKind Phase { get; private set; } = DatabasePhaseKind.AuthorizedReadCapability;
    internal long PhaseStarted { get; private set; } = NoPhaseStarted;
    internal bool PhaseActive { get; private set; }
    internal Exception? PrimaryError { get; set; }
    internal CancellationToken Token => linkedCancellation?.Token ?? requestToken;

    internal void Admit(NativeRequestWorkOwner owner, Guid requestId, NativeRequestWorkKind kind)
    {
        lease = owner.Acquire(requestId, kind);
        try
        {
            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(requestToken, owner.ShutdownToken);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            ThrowAdmissionFailure(error);
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            ThrowAdmissionFailure(error);
            throw;
        }
    }

    internal static NativeCapabilityWorkLifetime? Acquire(NativeRequestWorkOwner? owner, Guid requestId,
        NativeRequestWorkKind kind, CancellationToken requestToken)
    {
        if (owner is null)
        {
            return null;
        }

        var lifetime = new NativeCapabilityWorkLifetime(requestToken);
        lifetime.Admit(owner, requestId, kind);
        return lifetime;
    }

    internal void BeginPhase(DatabasePhaseKind phase)
    {
        Phase = phase;
        PhaseStarted = DatabasePhaseTelemetry.Begin();
        PhaseActive = true;
    }

    internal void CompletePhase()
    {
        DatabasePhaseTelemetry.End(Phase, DatabasePhaseOutcome.Completed, PhaseStarted);
        PhaseActive = false;
    }

    internal Action? FailureTelemetry()
    {
        if (!PhaseActive)
        {
            return null;
        }

        return PrimaryError is { } error
            ? () => EndFailedPhase(error)
            : () => DatabasePhaseTelemetry.End(Phase, DatabasePhaseOutcome.Faulted, PhaseStarted);
    }

    public void Dispose() => Settle(null, this, null, null);

    internal static Exception CombinePrimary(Exception? primary, Exception additional)
    {
        if (primary is null || ReferenceEquals(primary, additional))
        {
            return additional;
        }

        var fatal = CqrsRuntimeFailures.FindFatal(primary) ?? CqrsRuntimeFailures.FindFatal(additional);
        return fatal ?? new AggregateException(primary, additional);
    }

    internal static void Settle(Exception? primary, NativeCapabilityWorkLifetime? lifetime,
        Action? telemetry, Action? activation)
    {
        const int FailuresCountValidationBoundary = 0;

        var failures = default(NativeCapabilityCleanupFailures);
        Capture(telemetry, ref failures);
        Capture(activation, ref failures);
        lifetime?.CaptureRelease(ref failures);
        if (failures.Count > FailuresCountValidationBoundary)
        {
            ThrowCombined(primary, failures);
        }
    }

    private void EndFailedPhase(Exception error)
    {
        var outcome = error is OperationCanceledException
            ? DatabasePhaseTelemetry.CancellationOutcome(Token) : DatabasePhaseOutcome.Faulted;
        DatabasePhaseTelemetry.End(Phase, outcome, PhaseStarted);
    }

    private void ThrowAdmissionFailure(Exception primary)
    {
        var failures = default(NativeCapabilityCleanupFailures);
        CaptureRelease(ref failures);
        if (failures.Count > 0)
        {
            ThrowCombined(primary, failures);
        }

        ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private void CaptureRelease(ref NativeCapabilityCleanupFailures failures)
    {
        if (settled)
        {
            return;
        }

        settled = true;
        if (linkedCancellation is not null)
        {
            try
            {
                linkedCancellation.Dispose();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            linkedCancellation = null;
        }
        if (lease is not null)
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            lease = null;
        }
    }

    private static void Capture(Action? operation, ref NativeCapabilityCleanupFailures failures)
    {
        if (operation is null)
        {
            return;
        }

        try
        {
            operation();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    private static void ThrowCombined(Exception? primary, NativeCapabilityCleanupFailures cleanup)
    {
        const int EmptyFailuresCount = 1;
        const int IndexEmptyCount = 0;

        var fatal = CqrsRuntimeFailures.FindFatal(primary) ?? cleanup.FindFatal();
        if (fatal is not null)
        {
            ExceptionDispatchInfo.Capture(fatal).Throw();
        }

        var failures = new List<Exception>(cleanup.Count + (primary is null ? 0 : 1));
        if (primary is not null)
        {
            failures.Add(primary);
        }
        cleanup.AppendDistinct(primary, failures);
        if (failures.Count == EmptyFailuresCount)
        {
            ExceptionDispatchInfo.Capture(failures[IndexEmptyCount]).Throw();
        }
        throw new AggregateException(failures);
    }
}

/// <summary>Retains the four ordered cleanup failures possible for one capability settlement.</summary>
internal struct NativeCapabilityCleanupFailures
{
    private const string CleanupOverflowMessage = "Capability cleanup exceeded its fixed stages.";

    private Exception? first;
    private Exception? second;
    private Exception? third;
    private Exception? fourth;

    internal int Count { get; private set; }

    internal void Add(Exception error)
    {
        const int FirstCleanupFailureIndex = 0;
        const int SecondCleanupFailureIndex = 1;
        const int ThirdCleanupFailureIndex = 2;
        const int FourthCleanupFailureIndex = 3;

        switch (Count++)
        {
            case FirstCleanupFailureIndex:
                first = error;
                break;
            case SecondCleanupFailureIndex:
                second = error;
                break;
            case ThirdCleanupFailureIndex:
                third = error;
                break;
            case FourthCleanupFailureIndex:
                fourth = error;
                break;
            default:
                throw new InvalidOperationException(CleanupOverflowMessage);
        }
    }

    internal readonly Exception? FindFatal()
        => CqrsRuntimeFailures.FindFatal(first) ?? CqrsRuntimeFailures.FindFatal(second)
            ?? CqrsRuntimeFailures.FindFatal(third) ?? CqrsRuntimeFailures.FindFatal(fourth);

    internal readonly void AppendDistinct(Exception? primary, List<Exception> failures)
    {
        Append(first, primary, failures);
        Append(second, primary, failures);
        Append(third, primary, failures);
        Append(fourth, primary, failures);
    }

    private static void Append(Exception? error, Exception? primary, List<Exception> failures)
    {
        if (error is not null && !ReferenceEquals(error, primary))
        {
            failures.Add(error);
        }
    }
}
