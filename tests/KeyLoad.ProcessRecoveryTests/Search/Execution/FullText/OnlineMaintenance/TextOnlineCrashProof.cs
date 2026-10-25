using KeyLoad.Core;
using KeyLoad.Server.Search;

namespace KeyLoad.ProcessRecoveryTests.Search;

internal static class TextOnlineCrashProof
{
    private const byte ChecksumDamageMask = 1;
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

    internal static async Task CapturePublicationAsync(string root, TextOnlineCrashRuntime runtime,
        OnlineTextIndexMaintenanceRequest request, OnlineTextIndexMaintenanceResult receipt)
    {
        var database = runtime.Canonical.Database;
        var budget = new ReadExecutionBudget(database.EvaluationClock);
        var original = database.ReadOnlineTextOriginalPublication(CrashFixtureValues.Principal, request, budget)
            ?? throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        var path = Path.Combine(runtime.PhysicalRoot, TextOnlineRoot.DirectoryName,
            original.Authority.Leaf, TextOnlineStagingFiles.FileName);
        await TextIncrementalEvidenceFiles.WriteAsync(root, TextOnlineCrashScenario.ReceiptFile,
            receipt, CancellationToken.None);
        await TextIncrementalEvidenceFiles.WriteAsync(root, TextOnlineCrashScenario.PublicationLeafFile,
            original.Authority.Leaf, CancellationToken.None);
        await TextIncrementalEvidenceFiles.WriteAsync(root, TextOnlineCrashScenario.PublicationBytesFile,
            await File.ReadAllBytesAsync(path), CancellationToken.None);
    }

    internal static async Task ReplacePublicationAsync(string root, bool repair, CancellationToken token)
    {
        var leaf = await TextIncrementalEvidenceFiles.ReadAsync<string>(root,
            TextOnlineCrashScenario.PublicationLeafFile, token);
        var bytes = await TextIncrementalEvidenceFiles.ReadAsync<byte[]>(root,
            TextOnlineCrashScenario.PublicationBytesFile, token);
        if (!repair)
        {
            var envelope = OrleansSerialization.Deserialize<TextEnvelope>(bytes);
            var damaged = envelope.Sha256.ToArray();
            damaged[^1] ^= ChecksumDamageMask;
            bytes = OrleansSerialization.Serialize(envelope with { Sha256 = damaged });
        }
        var path = Path.Combine(root, TextOnlineRoot.DirectoryName, leaf, TextOnlineStagingFiles.FileName);
        await File.WriteAllBytesAsync(path, bytes, token);
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
