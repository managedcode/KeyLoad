using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Tracks a verified capability phase and joins its exact silo-local work lease.</summary>
internal sealed class NativeCapabilityWorkLifetime : IDisposable, IAsyncDisposable, INativeRequestWorkBinding
{
    private const int NoPhaseStarted = -1;

    private readonly NativeCapabilityWorkRegistration registration;
    private readonly Lock settlementGate = new();
    private Task? settlement;

    public NativeRequestWorkLease OriginalProducer => registration.OriginalProducer;
    public ResourceLifetimeWorkResultRetention ResultRetention => registration.ResultRetention;
    public void Bind(ResourceLifetimeCoverageV1 coverage) => registration.Bind(coverage);
    public void Bind(ImmutableArray<ResourceLifetimeV1> lifetimes) => registration.Bind(lifetimes);

    internal NativeCapabilityWorkLifetime(CancellationToken requestToken) => registration = new(requestToken);

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

    internal void Admit(NativeRequestWorkOwner owner, Guid requestId, NativeRequestWorkKind kind,
        IResourceLifetimeWorkBinding? binding = null) => registration.Admit(owner, requestId, kind, binding);

    internal static NativeCapabilityWorkLifetime? Acquire(NativeRequestWorkOwner? owner, Guid requestId,
        NativeRequestWorkKind kind, CancellationToken requestToken, IOptions<DatabaseLimits> limits)
    {
        if (owner is null)
        {
            return null;
        }

        var lifetime = new NativeCapabilityWorkLifetime(requestToken);
        try
        {
            lifetime.registration.Admit(owner, requestId, kind, ownedLimits: limits);
            return lifetime;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            lifetime.ThrowAdmissionFailure(error);
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
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

    public ValueTask DisposeAsync() => new(SettleAsync(PrimaryError, this, null, null));

    internal static Task SettleAsync(Exception? primary, NativeCapabilityWorkLifetime? lifetime,
        Action? telemetry, Action? activation)
    {
        if (lifetime is null)
        {
            return NativeCapabilityWorkSettlement.SettleAsync(primary, null, telemetry, activation);
        }
        lock (lifetime.settlementGate)
        {
            return lifetime.settlement ??= NativeCapabilityWorkSettlement.SettleAsync(primary, lifetime, telemetry, activation);
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

    internal static void Settle(Exception? primary, NativeCapabilityWorkLifetime? lifetime,
        Action? telemetry, Action? activation)
    {
        const int FailuresCountValidationBoundary = 0;

        var failures = default(NativeCapabilityCleanupFailures);
        NativeCapabilityWorkSettlement.Capture(telemetry, ref failures);
        NativeCapabilityWorkSettlement.Capture(activation, ref failures);
        lifetime?.CaptureRelease(ref failures);
        if (failures.Count > FailuresCountValidationBoundary)
        {
            NativeCapabilityWorkSettlement.ThrowCombined(primary, failures);
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
        const int NoCleanupFailures = 0;
        if (failures.Count > NoCleanupFailures)
        {
            NativeCapabilityWorkSettlement.ThrowCombined(primary, failures);
        }

        ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private void CaptureRelease(ref NativeCapabilityCleanupFailures failures) => registration.Release(ref failures);

    internal NativeRequestWorkLease? CaptureAsyncRelease(ref NativeCapabilityCleanupFailures failures)
        => registration.ReleaseAsyncState(ref failures);

    internal Task PrepareOwnedReleaseAsync() => registration.PrepareOwnedReleaseAsync();
    internal Exception? ReleaseOwnedRetention() => registration.ReleaseOwnedRetention();

}
