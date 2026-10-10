using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextOnlineCrashProof
{
    private const int CorruptTrailerDistance = 1;
    private const byte CorruptTrailerMask = 1;
    private const double LiteralExpectedSingleHitScore = 1d / 61d;
    private const string ChangedText = "ОНОВЛЕНО";
    private const string RemovedEnglishText = "hello";
    private const string RemovedUkrainianText = "ПРИВІТ";
    internal static async Task RecoverAsync(string root, NativeTextOnlineCrashRuntime runtime,
        NativeTextOnlineCrashOriginal original, bool verifyOnly)
    {
        var receipt = await NativeTextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceResult>(root,
            NativeTextOnlineCrashScenario.ReceiptFile, CancellationToken.None);
        NativeTextOnlineCrashFlow.RequireEqual(original.MutationReceipt,
            await runtime.Canonical.CommitAsync<CommitReceipt>(OperationKind.Batch, original.Mutation,
                original.Mutation.CommandId, CancellationToken.None));
        var replay = await NativeTextOnlineCrashFlow.RunAsync(runtime, original.Request, CancellationToken.None);
        NativeTextOnlineCrashFlow.RequireEqual(receipt, replay);
        await LiteralAsync(runtime, original.Request);
        if (verifyOnly)
        {
            var healthyRequest = await NativeTextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceRequest>(root,
                NativeTextOnlineCrashScenario.HealthyRequestFile, CancellationToken.None);
            var healthyReceipt = await NativeTextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceResult>(root,
                NativeTextOnlineCrashScenario.HealthyReceiptFile, CancellationToken.None);
            NativeTextOnlineCrashFlow.RequireEqual(healthyReceipt,
                await NativeTextOnlineCrashFlow.RunAsync(runtime, healthyRequest, CancellationToken.None));
            await LiteralAsync(runtime, healthyRequest);
            return;
        }
        await CorruptRepairAsync(runtime, original.Request);
        var healthy = original.Request with { CommandId = Guid.NewGuid() };
        var result = await NativeTextOnlineCrashFlow.RunAsync(runtime, healthy, CancellationToken.None);
        await LiteralAsync(runtime, healthy);
        NativeTextOnlineCrashFlow.RequireEqual(receipt,
            await NativeTextOnlineCrashFlow.RunAsync(runtime, original.Request, CancellationToken.None));
        await NativeTextIncrementalEvidenceFiles.WriteAsync(root, NativeTextOnlineCrashScenario.HealthyRequestFile,
            healthy, CancellationToken.None);
        await NativeTextIncrementalEvidenceFiles.WriteAsync(root, NativeTextOnlineCrashScenario.HealthyReceiptFile,
            result, CancellationToken.None);
    }

    private static async Task CorruptRepairAsync(NativeTextOnlineCrashRuntime runtime,
        OnlineTextIndexMaintenanceRequest request)
    {
        var database = runtime.Canonical.Database;
        var budget = new ReadExecutionBudget(runtime.Canonical.Options.Core.DatabaseLimits, database.EvaluationClock);
        var original = database.ReadOnlineTextOriginalPublication(CrashFixtureValues.Principal, request, budget)
            ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
        var path = Path.Combine(runtime.PhysicalRoot, NativeTextOnlineRoot.DirectoryName,
            original.Authority.Leaf, NativeTextOnlineStagingFiles.FileName);
        var bytes = await File.ReadAllBytesAsync(path);
        var invalid = bytes.ToArray();
        invalid[^CorruptTrailerDistance] ^= CorruptTrailerMask;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(path, invalid);
            var refused = false;
            try
            { _ = await NativeTextOnlineCrashFlow.RunAsync(runtime, request, CancellationToken.None); }
            catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption) { refused = true; }
            if (!refused)
            { throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid); }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(path, bytes), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        _ = await NativeTextOnlineCrashFlow.RunAsync(runtime, request, CancellationToken.None);
        await LiteralAsync(runtime, request);
    }

    private static async Task LiteralAsync(NativeTextOnlineCrashRuntime runtime, OnlineTextIndexMaintenanceRequest request)
    {
        var query = new SearchRequest(request.Consumer.Partition, request.Collection, request.Field, ChangedText);
        RankedDocument[] expected = [new(new(new(request.Consumer.Partition, request.Collection,
            NativeTextIncrementalCrashProtocol.Ukrainian), NativeTextIncrementalCrashProtocol.ChangedRevision,
            NativeTextIncrementalCrashProtocol.ChangedJson, false, []), LiteralExpectedSingleHitScore)];
        NativeTextOnlineCrashFlow.RequireEqual(expected,
            await runtime.Search.SearchAsync(CrashFixtureValues.Principal, query, CancellationToken.None));
        foreach (var removed in new[] { RemovedEnglishText, RemovedUkrainianText })
        {
            NativeTextOnlineCrashFlow.RequireEqual(Array.Empty<RankedDocument>(),
                await runtime.Search.SearchAsync(CrashFixtureValues.Principal, query with { Text = removed }, CancellationToken.None));
        }
    }
}
