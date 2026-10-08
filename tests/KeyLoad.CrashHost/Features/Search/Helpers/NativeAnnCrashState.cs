using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeAnnCrashState
{
    private const string ByteSeparator = ":";
    private const string ChangedToken = "changed-ann-process-token";
    internal static string Raw(DatabaseEngine database) => database.Store.Read(view =>
    {
        var page = view.Scan([], NativeAnnCrashContract.SnapshotBound);
        if (page.HasMore)
        { throw new InvalidOperationException(NativeAnnCrashContract.Invalid); }
        return string.Join(Environment.NewLine, page.Records.Select(row =>
            Convert.ToHexString(row.Key.Span) + ByteSeparator + Convert.ToHexString(row.Value.Span)));
    });
    internal static async Task<AnnMaintenanceCapabilityResult> PhaseAsync(DatabaseEngine database,
        NativeAnnCrashRuntime runtime, AnnMaintenanceRequest request, AnnMaintenanceCapabilityKind phase,
        ProjectionBatch? page = null, CommitProjectionBatchRequest? intent = null)
    {
        var before = Raw(database);
        var position = database.Store.Position;
        var result = await runtime.PhaseAsync(database, request, phase, page, intent);
        if (Raw(database) != before || database.Store.Position != position)
        { throw new InvalidOperationException(NativeAnnCrashContract.Invalid); }
        return result;
    }
    internal static async Task WriteAsync(string directory, string file, DatabaseEngine database,
        AnnMaintenanceRequest request, AnnMaintenanceCapabilityResult result, ProjectionBatchResult receipt,
        NativeAnnCrashRuntime runtime)
    {
        var seeds = runtime.Options.Core.AnnSeed;
        var seed = AnnSeedCollector.CapturePinned(database, CrashFixtureValues.Principal, request, seeds,
            new ReadExecutionBudget(CrashExecutionOptions.DatabaseLimits(database.Limits)));
        var snapshot = new NativeAnnCrashSnapshot(seed.Records.ToArray(), result.Source!, result.IndexSha256 ?? string.Empty,
            Convert.ToHexString(NativeSerialization.Serialize(receipt)), Raw(database), database.Store.Position);
        await File.WriteAllTextAsync(Path.Combine(directory, file), JsonSerializer.Serialize(snapshot, JsonDefaults.Options));
    }
    internal static ProjectionBatchResult CommitReplay(NativeAnnCrashCommandOwner commands, CommitProjectionBatchRequest intent)
    {
        var database = commands.Database;
        var original = NativeAnnCrashOperations.Commit(commands, intent);
        var before = Raw(database);
        var position = database.Store.Position;
        var replay = NativeAnnCrashOperations.Commit(commands, intent);
        if (!NativeSerialization.Serialize(original).AsSpan().SequenceEqual(NativeSerialization.Serialize(replay))
            || Raw(database) != before || database.Store.Position != position)
        { throw new InvalidOperationException(NativeAnnCrashContract.Invalid); }
        var changed = intent with { Token = ChangedToken };
        if (NativeAnnCrashOperations.Apply(commands, OperationKind.CommitProjectionBatch, changed, intent.CommandId).Error != ErrorCode.Conflict
            || Raw(database) != before || database.Store.Position != position)
        { throw new InvalidOperationException(NativeAnnCrashContract.Invalid); }
        return original;
    }
}
