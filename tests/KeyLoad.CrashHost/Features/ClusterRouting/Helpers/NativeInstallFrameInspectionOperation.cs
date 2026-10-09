using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Storage.IO;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class NativeInstallFrameInspectionOperation
{
    private const int NoFailures = 0;
    private const int FirstFailure = 0;
    private const string InvalidOutcome = "The original native frame outcome scope is invalid.";

    internal static NativeInstallFrameInspectionReceipt Run(NativeInstallFrameInspectionRequest request,
        C1OutcomeInspectionFailureEvidence evidence)
    {
        var options = new ZoneTreeStoreOptions(request.Directory)
        { Incarnation = request.Incarnation, MaxFrameBytes = request.MaximumFrameBytes }
            .ResolveExecutionOptions(CrashExecutionOptions.StorageExecution());
        var failures = new List<Exception>();
        NativeInstallFrameInspectionReceipt? receipt = null;
        ServerFailureObserver.Observe(() =>
        {
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.OpenStore);
            using var ownership = OfflineRegularFile.Open(Path.Combine(request.Directory, ZoneTreePersistenceFormat.OwnerLockFileName),
                FileAccess.ReadWrite, FileShare.None, options.FileBufferBytes);
            ServerFailureObserver.Observe(() =>
            {
                evidence.SetPhase(C1OutcomeInspectionFailurePhase.ReadOutcome);
                var frame = ZoneTreeNativeFrameInspection.Read(options, request.ExpectedNodeId, ownership,
                    KeySpace.PartitionOutcome(request.Partition, request.PrincipalId, request.CommandId), TimeProvider.System, CancellationToken.None);
                receipt = RequireOutcome(frame, request);
            }, failures);
            if (failures.Count > NoFailures)
            { evidence.Capture(failures[FirstFailure]); }
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.DisposeStore);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return receipt ?? throw Errors.Fail(ErrorCode.Corruption, InvalidOutcome);
    }

    private static NativeInstallFrameInspectionReceipt RequireOutcome(ZoneTreeNativeFrameInspectionResult frame,
        NativeInstallFrameInspectionRequest request)
    {
        var actual = NativeSerialization.Deserialize<StoredOutcome>(frame.OutcomeBytes.Span);
        if (actual.Incarnation != request.Incarnation || actual.ScopeKind != CommandOutcomeScopeKind.Partition
            || actual.Partition != request.Partition || actual.Result is null
            || actual.Result.Error is not null && (actual.Result.NativeValue is not null || actual.Result.Json is not null))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutcome); }
        var phase = actual.Result.Error is null ? actual.Result.NativeValue as PartitionMovePhaseResult : null;
        if (phase is not null && phase.Journal.CommandId != request.CommandId)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutcome); }
        return new(C1OutcomeInspectionProtocol.Version, frame.NodeId, frame.Incarnation, request.CommandId,
            frame.FormatVersion, frame.Sequence, frame.PayloadBytes, frame.PayloadSha256, frame.MutationCount,
            frame.RawMutationBytes, frame.ExaminedFrames, frame.ExaminedBytes, frame.MaximumObservedPayloadBytes,
            actual.Result.Error is { } error ? (int)error : NativeInstallFrameInspectionProtocol.NoErrorOrStage,
            phase is not null ? (int)phase.Stage : NativeInstallFrameInspectionProtocol.NoErrorOrStage, phase?.InstalledReceipt is not null,
            actual.Result.Error == ErrorCode.ResourceExhausted
                && string.Equals(actual.Result.SafeDetail, ZoneTreePersistenceFormat.EncodedTransactionFrameLimitExceeded, StringComparison.Ordinal));
    }
}
