using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Search;

namespace KeyLoad.ProcessRecoveryTests.Search;

internal static class AnnCrashState
{
    private const string ByteSeparator = ":";
    private const string ChangedToken = "changed-ann-process-token";
    internal static string Raw(DatabaseEngine database) => database.Store.Read(view =>
    {
        var page = view.Scan([], AnnCrashContract.SnapshotBound);
        if (page.HasMore)
        {
            throw new InvalidOperationException(AnnCrashContract.Invalid);
        }
        return string.Join(Environment.NewLine, page.Records.Select(row =>
            Convert.ToHexString(row.Key.Span) + ByteSeparator + Convert.ToHexString(row.Value.Span)));
    });
    internal static async Task<AnnMaintenanceCapabilityResult> PhaseAsync(DatabaseEngine database,
        AnnCrashRuntime runtime, AnnMaintenanceRequest request, AnnMaintenanceCapabilityKind phase,
        ProjectionBatch? page = null, CommitProjectionBatchRequest? intent = null)
    {
        var before = Raw(database);
        var position = database.Store.Position;
        var result = await runtime.PhaseAsync(database, request, phase, page, intent);
        if (Raw(database) != before || database.Store.Position != position)
        {
            throw new InvalidOperationException(AnnCrashContract.Invalid);
        }
        return result;
    }
    internal static async Task WriteAsync(string directory, string file, DatabaseEngine database,
        AnnMaintenanceRequest request, AnnMaintenanceCapabilityResult result, ProjectionBatchResult receipt,
        AnnCrashRuntime runtime)
    {
        var seeds = runtime.Options.Core.AnnSeed;
        var seed = AnnSeedCollector.CapturePinned(database, CrashFixtureValues.Principal, request, seeds,
            new ReadExecutionBudget());
        var snapshot = new AnnCrashSnapshot(seed.Records.ToArray(), result.Source!, result.IndexSha256 ?? string.Empty,
            Convert.ToHexString(OrleansSerialization.Serialize(receipt)), Raw(database), database.Store.Position);
        await File.WriteAllTextAsync(Path.Combine(directory, file), JsonSerializer.Serialize(snapshot, JsonDefaults.Options));
    }
    internal static ProjectionBatchResult CommitReplay(AnnCrashCommandOwner commands, CommitProjectionBatchRequest intent)
    {
        var database = commands.Database;
        var original = AnnCrashOperations.Commit(commands, intent);
        var before = Raw(database);
        var position = database.Store.Position;
        var replay = AnnCrashOperations.Commit(commands, intent);
        if (!OrleansSerialization.Serialize(original).AsSpan().SequenceEqual(OrleansSerialization.Serialize(replay))
            || Raw(database) != before || database.Store.Position != position)
        {
            throw new InvalidOperationException(AnnCrashContract.Invalid);
        }
        var changed = intent with
        {
            Token = ChangedToken
        };
        if (Errors.Code(AnnCrashOperations.Apply(commands, OperationKind.CommitProjectionBatch, changed, intent.CommandId).Problem).Value != ErrorCode.Conflict
            || Raw(database) != before || database.Store.Position != position)
        {
            throw new InvalidOperationException(AnnCrashContract.Invalid);
        }
        return original;
    }
}
