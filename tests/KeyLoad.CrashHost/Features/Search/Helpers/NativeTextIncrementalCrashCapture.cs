using System.Text.Json;
using KeyLoad.Orleans;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextIncrementalCrashCapture
{
    internal static async Task WriteAsync(string root, string file, NativeTextIncrementalCrashRuntime runtime,
        NativeTextIncrementalCrashOriginal original,
        (TextMaintenanceCapabilityResult State, ProjectionBatchResult? Receipt) completed)
    {
        var operation = runtime.Database.NormalizeOperation(new ReplicatedOperation(original.Mutation.CommandId,
            OperationKind.Batch, CrashFixtureValues.Principal, runtime.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(original.Mutation, JsonDefaults.Options)));
        var replay = runtime.Database.ResolveOutcome(operation).Get<CommitReceipt>();
        if (!NativeSerialization.Serialize(replay).AsSpan().SequenceEqual(NativeSerialization.Serialize(original.Receipt)))
        { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
        var ukrainian = runtime.Database.GetDocument(CrashFixtureValues.Principal,
            new EntityRef(NativeTextIncrementalCrashProtocol.Partition, NativeTextIncrementalCrashProtocol.Collection,
                NativeTextIncrementalCrashProtocol.Ukrainian))
            ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
        var english = runtime.Database.GetDocument(CrashFixtureValues.Principal,
            new EntityRef(NativeTextIncrementalCrashProtocol.Partition, NativeTextIncrementalCrashProtocol.Collection,
                NativeTextIncrementalCrashProtocol.English));
        var inventory = await NativeTextIncrementalCrashInventory.ReadAsync(runtime, original.Request);
        var checkpoint = await NativeTextIncrementalCheckpointEvidence.ResolveOriginalAsync(runtime,
            original.Request, completed.Receipt, CancellationToken.None);
        var result = new NativeTextIncrementalCrashResult(completed.State, replay, checkpoint,
            ukrainian, english, runtime.Database.LastApplied, inventory.Records, inventory.Postings,
            NativeTextCanonicalStateCapture.Capture(runtime.Node.Canonical), runtime.Database.Store.Position,
            await runtime.SelectedQueryAsync(original.Request, ukrainian.Revision == NativeTextIncrementalCrashProtocol.HealthyRevision
                ? NativeTextIncrementalCrashQuery.HealthyTerm : NativeTextIncrementalCrashQuery.ChangedTerm),
            await runtime.SelectedQueryAsync(original.Request, NativeTextIncrementalCrashQuery.RemovedUkrainian),
            await runtime.SelectedQueryAsync(original.Request, NativeTextIncrementalCrashQuery.RemovedEnglish));
        await NativeTextIncrementalEvidenceFiles.WriteAsync(root, file, result, CancellationToken.None);
    }

    internal static async Task HealthyAsync(string root, NativeTextIncrementalCrashRuntime runtime,
        NativeTextIncrementalCrashOriginal original)
    {
        var command = new CommandRequest(Guid.NewGuid(), NativeTextIncrementalCrashProtocol.Partition,
            [new PutDocument(NativeTextIncrementalCrashProtocol.Collection, NativeTextIncrementalCrashProtocol.Ukrainian,
                NativeTextIncrementalCrashProtocol.HealthyJson,
                ExpectedRevision: NativeTextIncrementalCrashProtocol.ChangedRevision)]);
        var receipt = await runtime.CommitAsync<CommitReceipt>(OperationKind.Batch, command, command.CommandId,
            CancellationToken.None);
        var request = original.Request with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Restore };
        var completed = await NativeTextIncrementalCrashReplay.FinishAsync(runtime, request, CancellationToken.None);
        await WriteAsync(root, NativeTextIncrementalCrashProtocol.HealthyFile, runtime,
            new(request, command, receipt), completed);
        await NativeTextIncrementalEvidenceFiles.WriteAsync(root, NativeTextIncrementalCrashProtocol.RequestFile,
            new NativeTextIncrementalCrashOriginal(request, command, receipt), CancellationToken.None);
    }
}
