using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.ProcessRecoveryTests.Search;

internal static class TextIncrementalCrashCapture
{
    internal static async Task WriteAsync(string root, string file, TextIncrementalCrashRuntime runtime,
        TextIncrementalCrashOriginal original,
        (TextMaintenanceCapabilityResult State, ProjectionBatchResult? Receipt) completed)
    {
        var operation = runtime.Database.OwnCommand(runtime.Database.CreateCommand(OperationKind.Batch, original.Mutation.CommandId, CrashFixtureValues.Principal, runtime.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(original.Mutation, JsonDefaults.Options)));
        var replay = runtime.Database.ResolveOutcome(operation).Get<CommitReceipt>();
        if (!OrleansSerialization.Serialize(replay).AsSpan().SequenceEqual(OrleansSerialization.Serialize(original.Receipt)))
        {
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
        var ukrainian = runtime.Database.GetDocument(CrashFixtureValues.Principal,
            new EntityRef(TextIncrementalCrashProtocol.Partition, TextIncrementalCrashProtocol.Collection,
                TextIncrementalCrashProtocol.Ukrainian))
            ?? throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        var english = runtime.Database.GetDocument(CrashFixtureValues.Principal,
            new EntityRef(TextIncrementalCrashProtocol.Partition, TextIncrementalCrashProtocol.Collection,
                TextIncrementalCrashProtocol.English));
        var inventory = await TextIncrementalCrashInventory.ReadAsync(runtime, original.Request);
        var checkpoint = await TextIncrementalCheckpointEvidence.ResolveOriginalAsync(runtime,
            original.Request, completed.Receipt, CancellationToken.None);
        var result = new TextIncrementalCrashResult(completed.State, replay, checkpoint,
            ukrainian, english, runtime.Database.LastApplied, inventory.Records, inventory.Postings,
            TextCanonicalStateCapture.Capture(runtime.Store), runtime.Database.Store.Position,
            await runtime.SelectedQueryAsync(original.Request, ukrainian.Revision == TextIncrementalCrashProtocol.HealthyRevision
                ? TextIncrementalCrashQuery.HealthyTerm : TextIncrementalCrashQuery.ChangedTerm),
            await runtime.SelectedQueryAsync(original.Request, TextIncrementalCrashQuery.RemovedUkrainian),
            await runtime.SelectedQueryAsync(original.Request, TextIncrementalCrashQuery.RemovedEnglish));
        await TextIncrementalEvidenceFiles.WriteAsync(root, file, result, CancellationToken.None);
    }

    internal static async Task HealthyAsync(string root, TextIncrementalCrashRuntime runtime,
        TextIncrementalCrashOriginal original)
    {
        var command = new CommandRequest(Guid.NewGuid(), TextIncrementalCrashProtocol.Partition,
            [new PutDocument(TextIncrementalCrashProtocol.Collection, TextIncrementalCrashProtocol.Ukrainian,
                TextIncrementalCrashProtocol.HealthyJson,
                ExpectedRevision: TextIncrementalCrashProtocol.ChangedRevision)]);
        var receipt = await runtime.CommitAsync<CommitReceipt>(OperationKind.Batch, command, command.CommandId,
            CancellationToken.None);
        var request = original.Request with
        {
            CommandId = Guid.NewGuid(),
            Mode = TextIndexMaintenanceMode.Restore
        };
        var completed = await TextIncrementalCrashReplay.FinishAsync(runtime, request, CancellationToken.None);
        await WriteAsync(root, TextIncrementalCrashProtocol.HealthyFile, runtime,
            new(request, command, receipt), completed);
        await TextIncrementalEvidenceFiles.WriteAsync(root, TextIncrementalCrashProtocol.RequestFile,
            new TextIncrementalCrashOriginal(request, command, receipt), CancellationToken.None);
    }
}
