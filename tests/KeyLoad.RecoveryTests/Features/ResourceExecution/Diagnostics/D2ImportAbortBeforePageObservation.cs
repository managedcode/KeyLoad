using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

/// <summary>Reports bounded original cleanup cancellation context without reading records or replaying work.</summary>
internal static class D2ImportAbortBeforePageObservation
{
    private const string CancellationKind = "D2ImportAbortBeforePageCancellation";
    private const string TerminalFamily = "terminal";
    private const string OutputExceeded = "The native Abort cancellation witness exceeded its retained diagnostic bound.";
    private const int MaximumCharacters = 1024;
    private const int FirstFamily = 0;

    internal static async Task<(PartitionMovePhaseResult Result, ReplicatedOperation Original, Guid GrantId,
        OperationResult Outcome)> EffectAsync(D2ImportSession session, PartitionMovePhaseResult state,
        ReadOnlyMemory<byte> body, int family, int batch, long started, CancellationToken token)
    {
        try
        {
            return await D2ImportPhases.EffectAsync(session, state,
                D2ImportPhases.Phase(session, state, PartitionMovePeerStage.Abort, body, batch), atSource: false, token);
        }
        catch (OperationCanceledException primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() => Write(session, family, batch, started, primary, token), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static void Write(D2ImportSession session, int family, int batch, long started,
        OperationCanceledException original, CancellationToken token)
    {
        var source = session.Source.Journal.Log.State;
        var target = session.Target.Journal.Log.State;
        var text = JsonSerializer.Serialize(new
        {
            Kind = CancellationKind,
            FamilyOrdinal = family,
            Family = family >= FirstFamily && family < PartitionMoveCleanupFamilies.All.Length
                ? PartitionMoveCleanupFamilies.All[family] : TerminalFamily,
            BatchOrdinal = batch,
            CleanupElapsedMilliseconds = TimeProvider.System.GetElapsedTime(started).TotalMilliseconds,
            SourceLastIndex = source.LastIndex,
            SourceCommittedIndex = source.CommittedIndex,
            TargetLastIndex = target.LastIndex,
            TargetCommittedIndex = target.CommittedIndex,
            OriginalCallerTokenCancelled = token.IsCancellationRequested,
            CaughtTokenCancelled = original.CancellationToken.IsCancellationRequested
        });
        if (text.Length > MaximumCharacters)
        {
            throw new InvalidDataException(OutputExceeded);
        }
        Console.WriteLine(text);
    }
}
