using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Diagnostics.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Tracks a verified capability phase and joins its exact silo-local work lease.</summary>
internal sealed class CapabilityWorkLifetime : IDisposable, IAsyncDisposable, IRequestWorkBinding
{
    private const int NoPhaseStarted = -1;

    private readonly CapabilityWorkRegistration registration;
    private readonly Lock settlementGate = new();
    private Task? settlement;

    public RequestWorkLease OriginalProducer => registration.OriginalProducer;
    public ResourceLifetimeWorkResultRetention ResultRetention => registration.ResultRetention;
    public void Bind(ResourceLifetimeCoverage coverage) => registration.Bind(coverage);
    public void Bind(ImmutableArray<ResourceLifetime> lifetimes) => registration.Bind(lifetimes);

    internal CapabilityWorkLifetime(CancellationToken requestToken) => registration = new(requestToken);

    internal GrainFailureStage Stage { get; set; } = GrainFailureStage.EnvelopeVerification;
    internal Guid RequestId
    {
        get; set;
    }
    internal DatabasePhaseKind Phase { get; private set; } = DatabasePhaseKind.AuthorizedReadCapability;
    internal long PhaseStarted { get; private set; } = NoPhaseStarted;
    internal bool PhaseActive
    {
        get; private set;
    }
    internal Exception? PrimaryError
    {
        get; set;
    }
    internal CancellationToken Token => registration.Token;

    internal void Admit(RequestWorkOwner owner, Guid requestId, RequestWorkKind kind,
        IResourceLifetimeWorkBinding? binding = null) => registration.Admit(owner, requestId, kind, binding);

    internal static global::ManagedCode.Communication.Result<CapabilityWorkLifetime?> Acquire(RequestWorkOwner? owner, Guid requestId,
        RequestWorkKind kind, CancellationToken token)
    {
        if (owner is null)
        {
            return global::ManagedCode.Communication.Result<CapabilityWorkLifetime?>.FailNotFound(nameof(Acquire));
        }

        var lifetime = new CapabilityWorkLifetime(token);
        try
        {
            lifetime.registration.Admit(owner, requestId, kind, ownRetention: true);
            return lifetime;
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            lifetime.ThrowAdmissionFailure(error);
            throw;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            lifetime.ThrowAdmissionFailure(error);
            throw;
        }
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

    internal global::ManagedCode.Communication.Result<Action?> FailureTelemetry()
    {
        if (!PhaseActive)
        {
            return global::ManagedCode.Communication.Result<Action?>.FailNotFound(nameof(FailureTelemetry));
        }

        return PrimaryError is { } error
            ? ((Action?)(() => EndFailedPhase(error))) is { } outcomeValue2 ? global::ManagedCode.Communication.Result<Action?>.Succeed(outcomeValue2) : global::ManagedCode.Communication.Result<Action?>.FailNotFound(nameof(FailureTelemetry))
            : ((Action?)(() => DatabasePhaseTelemetry.End(Phase, DatabasePhaseOutcome.Faulted, PhaseStarted))) is { } outcomeValue3 ? global::ManagedCode.Communication.Result<Action?>.Succeed(outcomeValue3) : global::ManagedCode.Communication.Result<Action?>.FailNotFound(nameof(FailureTelemetry));
    }

    public void Dispose() => registration.Dispose();

    public ValueTask DisposeAsync() => new(SettleAsync(PrimaryError, this, null, null));

    internal static Task SettleAsync(Exception? primary, CapabilityWorkLifetime? lifetime,
        Action? telemetry, Action? activation)
    {
        if (lifetime is null)
        {
            return CapabilityWorkSettlement.SettleAsync(primary, null, telemetry, activation);
        }
        lock (lifetime.settlementGate)
        {
            return lifetime.settlement ??= CapabilityWorkSettlement.SettleAsync(primary, lifetime, telemetry, activation);
        }
    }

    internal static Exception CombinePrimary(Exception? primary, Exception additional)
    {
        if (primary is null || ReferenceEquals(primary, additional))
        {
            return additional;
        }

        var fatal = CqrsRuntimeFailures.FindFatal(primary) ?? CqrsRuntimeFailures.FindFatal(additional);
        return fatal ?? new AggregateException(primary, additional);
    }

    internal static void Settle(Exception? primary, CapabilityWorkLifetime? lifetime,
        Action? telemetry, Action? activation)
    {
        const int FailuresCountValidationBoundary = 0;

        var failures = default(CapabilityCleanupFailures);
        CapabilityWorkSettlement.Capture(telemetry, ref failures);
        CapabilityWorkSettlement.Capture(activation, ref failures);
        lifetime?.CaptureRelease(ref failures);
        if (failures.Count > FailuresCountValidationBoundary)
        {
            CapabilityWorkSettlement.ThrowCombined(primary, failures);
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
        var failures = default(CapabilityCleanupFailures);
        CaptureRelease(ref failures);
        const int NoCleanupFailures = 0;
        if (failures.Count > NoCleanupFailures)
        {
            CapabilityWorkSettlement.ThrowCombined(primary, failures);
        }

        ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private void CaptureRelease(ref CapabilityCleanupFailures failures) => registration.Release(ref failures);

    internal RequestWorkLease? CaptureAsyncRelease(ref CapabilityCleanupFailures failures)
        => (registration.ReleaseAsyncState(ref failures)).Value;

    internal Task PrepareOwnedReleaseAsync() => registration.PrepareOwnedReleaseAsync();
    internal Exception? ReleaseOwnedRetention() => (registration.ReleaseOwnedRetention()).Value;

}
