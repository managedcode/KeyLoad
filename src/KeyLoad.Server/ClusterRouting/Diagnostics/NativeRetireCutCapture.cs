using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Copies one complete bounded native owner cut without submitting or replacing any database operation.</summary>
internal static class NativeRetireCutCapture
{
    internal static NativeRetireCutWitness Read(PartitionHost owner, ReplicaSiloDiscovery actual,
        NativeRetireCutRequest request, ReplicaNodeState before, RequestCqrsProbeObserver observer, CancellationToken token)
    {
        var database = owner.Database;
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        return database.Store.Read(view =>
        {
            budget.Check();
            var identity = database.Store.Identity;
            if (actual.Incarnation != identity.Incarnation || !actual.TransportReady)
            {
                throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
            }
            var rows = new List<NativeCanonicalModelCutRow>();
            var scan = budget.VisitRange(view, [], database.Limits.MaxScanRecords, (key, value) =>
            {
                budget.ChargeBytes(checked(key.Length + value.Length));
                rows.Add(new(key.ToArray(), value.ToArray()));
                return true;
            });
            if (scan.HasMore || scan.StoppedByVisitor)
            {
                throw NativeCanonicalModelCutCodec.Exhausted();
            }
            var original = ReadOriginal(database, observer, request, budget);
            var installed = ReadReceipt(owner, request, before, budget);
            var result = new NativeRetireCutWitness(request, actual, database.Store.Position, before.LastIndex,
                identity with
                {
                    SigningKey = default
                }, Convert.ToHexString(SHA256.HashData(identity.SigningKey.Span)),
                rows.ToImmutableArray(), original, installed, before.CommittedIndex);
            if (NativeSerialization.Measure(result) > database.Limits.MaxQueryReadBytes)
            {
                throw NativeCanonicalModelCutCodec.Exhausted();
            }
            budget.Check();
            return result;
        });
    }

    private static ReadOnlyMemory<byte>? ReadOriginal(DatabaseEngine database, RequestCqrsProbeObserver observer,
        NativeRetireCutRequest request, ReadExecutionBudget budget)
    {
        var bytes = observer.RetireSealed.Read(request);
        if (bytes is not { } captured)
        {
            return null;
        }
        budget.ChargeBytes(captured.Length);
        var original = database.VerifyOperationAuthority(NativeSerialization.Deserialize<ReplicatedOperation>(captured.Span));
        if (original.Id != request.CommandId || original.PrincipalId != request.PrincipalId
            || original.Kind != OperationKind.PartitionMovementPhase)
        {
            throw NativeRetireCutWire.Invalid();
        }
        var phase = PartitionMovementNativePayload.Read(original);
        if (phase.Stage != PartitionMovePeerStage.Retire || phase.ReceiverEffectAdmission is null)
        {
            throw NativeRetireCutWire.Invalid();
        }
        budget.Check();
        return captured;
    }

    private static ReplicaEntry? ReadReceipt(PartitionHost owner, NativeRetireCutRequest request,
        ReplicaNodeState before, ReadExecutionBudget budget)
    {
        if (request.ReceiptToken is not { } installed || installed.Incarnation != owner.Database.Store.Identity.Incarnation)
        {
            return null;
        }
        var log = owner.Materializer.Log;
        if (installed.Position > before.CommittedIndex || log.State.LastIndex != before.LastIndex
            || log.State.CommittedIndex != before.CommittedIndex)
        {
            throw NativeCanonicalModelCutDiagnosticRead.Unavailable();
        }
        var entry = log.ReadEntry(installed.Position, budget.ChargeBytes)
            ?? throw NativeRetireCutWire.Invalid();
        budget.Check();
        return entry;
    }
}
