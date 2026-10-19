using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class TextOnlineCrashProof
{
    private const int CorruptTrailerDistance = 1;
    private const byte CorruptTrailerMask = 1;
    private const double LiteralExpectedSingleHitScore = 1d / 61d;
    private const string ChangedText = "ОНОВЛЕНО";
    private const string RemovedEnglishText = "hello";
    private const string RemovedUkrainianText = "ПРИВІТ";
    internal static async Task RecoverAsync(string root, TextOnlineCrashRuntime runtime,
        TextOnlineCrashOriginal original, bool verifyOnly)
    {
        var receipt = await TextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceResult>(root,
            TextOnlineCrashScenario.ReceiptFile, CancellationToken.None);
        TextOnlineCrashFlow.RequireEqual(original.MutationReceipt,
            await runtime.Canonical.CommitAsync<CommitReceipt>(OperationKind.Batch, original.Mutation,
                original.Mutation.CommandId, CancellationToken.None));
        var replay = await TextOnlineCrashFlow.RunAsync(runtime, original.Request, CancellationToken.None);
        TextOnlineCrashFlow.RequireEqual(receipt, replay);
        await LiteralAsync(runtime, original.Request);
        if (verifyOnly)
        {
            var healthyRequest = await TextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceRequest>(root,
                TextOnlineCrashScenario.HealthyRequestFile, CancellationToken.None);
            var healthyReceipt = await TextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceResult>(root,
                TextOnlineCrashScenario.HealthyReceiptFile, CancellationToken.None);
            TextOnlineCrashFlow.RequireEqual(healthyReceipt,
                await TextOnlineCrashFlow.RunAsync(runtime, healthyRequest, CancellationToken.None));
            await LiteralAsync(runtime, healthyRequest);
            return;
        }
        await CorruptRepairAsync(runtime, original.Request);
        var healthy = original.Request with
        {
            CommandId = Guid.NewGuid()
        };
        var result = await TextOnlineCrashFlow.RunAsync(runtime, healthy, CancellationToken.None);
        await LiteralAsync(runtime, healthy);
        TextOnlineCrashFlow.RequireEqual(receipt,
            await TextOnlineCrashFlow.RunAsync(runtime, original.Request, CancellationToken.None));
        await TextIncrementalEvidenceFiles.WriteAsync(root, TextOnlineCrashScenario.HealthyRequestFile,
            healthy, CancellationToken.None);
        await TextIncrementalEvidenceFiles.WriteAsync(root, TextOnlineCrashScenario.HealthyReceiptFile,
            result, CancellationToken.None);
    }

    private static async Task CorruptRepairAsync(TextOnlineCrashRuntime runtime,
        OnlineTextIndexMaintenanceRequest request)
    {
        var database = runtime.Canonical.Database;
        var budget = new ReadExecutionBudget(runtime.Canonical.Options.Core.DatabaseLimits, database.EvaluationClock);
        var original = database.ReadOnlineTextOriginalPublication(CrashFixtureValues.Principal, request, budget)
            ?? throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        var path = Path.Combine(runtime.PhysicalRoot, TextOnlineRoot.DirectoryName,
            original.Authority.Leaf, TextOnlineStagingFiles.FileName);
        var bytes = await File.ReadAllBytesAsync(path);
        var invalid = bytes.ToArray();
        invalid[^CorruptTrailerDistance] ^= CorruptTrailerMask;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(path, invalid);
            var refused = false;
            try
            {
                _ = await TextOnlineCrashFlow.RunAsync(runtime, request, CancellationToken.None);
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption) { refused = true; }
            if (!refused)
            {
                throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(path, bytes), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        _ = await TextOnlineCrashFlow.RunAsync(runtime, request, CancellationToken.None);
        await LiteralAsync(runtime, request);
    }

    private static async Task LiteralAsync(TextOnlineCrashRuntime runtime, OnlineTextIndexMaintenanceRequest request)
    {
        var query = new SearchRequest(request.Consumer.Partition, request.Collection, request.Field, ChangedText);
        var lifetime = await TextIncrementalProcessAssertions.ReadLifetimeAsync(
            runtime.Canonical.EvidenceRoot, CancellationToken.None);
        RankedDocument[] expected = [new(new(new(request.Consumer.Partition, request.Collection,
            TextIncrementalCrashProtocol.Ukrainian)
        {
            Lifetime = lifetime
        }, TextIncrementalCrashProtocol.ChangedRevision,
            TextIncrementalCrashProtocol.ChangedJson, false, []), LiteralExpectedSingleHitScore)];
        TextOnlineCrashFlow.RequireEqual(expected,
            await runtime.Search.SearchAsync(CrashFixtureValues.Principal, query, CancellationToken.None));
        foreach (var removed in new[] { RemovedEnglishText, RemovedUkrainianText })
        {
            TextOnlineCrashFlow.RequireEqual(Array.Empty<RankedDocument>(),
                await runtime.Search.SearchAsync(CrashFixtureValues.Principal, query with
                {
                    Text = removed
                }, CancellationToken.None));
        }
    }
}
