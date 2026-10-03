using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-004/005: genuine callers preserve absent-create, revision CAS, command identity and exact bodies.</summary>
internal static class IsolatedKeyLoadPublicRegressionDocuments
{
    private const string PutKind = "putDocument";
    private const string DeleteKind = "deleteDocument";

    internal static async Task VerifyAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicRegressionMcp mcp,
        IsolatedKeyLoadPublicRegressionScenario scenario, CancellationToken token)
    {
        var create = scenario.Command(new PutDocument(scenario.Document.Collection, scenario.Document.Id, scenario.InitialJson, 0),
            new PutDocument(scenario.Sentinel.Collection, scenario.Sentinel.Id, scenario.SentinelJson, 0));
        var receipt = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.CommitAsync(create, token));
        await IsolatedKeyLoadPublicRegressionAssertions.ReceiptAsync(receipt, create.CommandId, 2, scenario.Partition);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(PutKind, scenario.Document.Collection, scenario.Document.Id, 1));
        await Assert.That(receipt.Mutations[1]).IsEqualTo(new MutationReceipt(PutKind, scenario.Sentinel.Collection, scenario.Sentinel.Id, 1));
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(receipt,
            await mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicRegressionProtocol.Commit, create, token));
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(receipt,
            await IsolatedKeyLoadPublicRegressionProtocol.SqlAsync<CommitReceipt>(sdk,
                IsolatedKeyLoadPublicRegressionProtocol.Call(scenario.Partition, IsolatedKeyLoadPublicRegressionProtocol.Commit, create), token));
        var initial = await mcp.SuccessAsync<DocumentResult>(IsolatedKeyLoadPublicRegressionProtocol.Get,
            new GetDocumentRequest(scenario.Document), token);
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(initial, scenario.Document, scenario.InitialStoredJson, 1);
        await VerifyUpdateAsync(sdk, mcp, scenario, token);
        await VerifyRejectedMutationsAsync(sdk, mcp, scenario, token);
        await VerifyDeleteAsync(sdk, mcp, scenario, token);
    }

    private static async Task VerifyUpdateAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicRegressionMcp mcp,
        IsolatedKeyLoadPublicRegressionScenario scenario, CancellationToken token)
    {
        var update = scenario.Command(new PutDocument(scenario.Document.Collection, scenario.Document.Id,
            scenario.UpdatedJson, 1, ExplicitReplacement: true));
        var receipt = await mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicRegressionProtocol.Commit, update, token);
        await IsolatedKeyLoadPublicRegressionAssertions.ReceiptAsync(receipt, update.CommandId, 1, scenario.Partition);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(PutKind, scenario.Document.Collection, scenario.Document.Id, 2));
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(receipt,
            await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.CommitAsync(update, token)));
        var document = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.GetAsync(scenario.Document, token));
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(document!, scenario.Document, scenario.UpdatedStoredJson, 2);
    }

    private static async Task VerifyRejectedMutationsAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicRegressionMcp mcp,
        IsolatedKeyLoadPublicRegressionScenario scenario, CancellationToken token)
    {
        var duplicate = scenario.Command(new PutDocument(scenario.Document.Collection, scenario.Document.Id, scenario.InitialJson, 0));
        await IsolatedKeyLoadPublicRegressionAssertions.ErrorAsync(await sdk.CommitAsync(duplicate, token), ErrorCode.RevisionConflict);
        await mcp.ErrorAsync(IsolatedKeyLoadPublicRegressionProtocol.Commit, duplicate, ErrorCode.RevisionConflict, token);
        var updateMissing = scenario.Command(new PutDocument(scenario.Missing.Collection, scenario.Missing.Id,
            scenario.UpdatedJson, 1, ExplicitReplacement: true));
        await IsolatedKeyLoadPublicRegressionAssertions.ErrorAsync(await sdk.CommitAsync(updateMissing, token), ErrorCode.RevisionConflict);
        var deleteMissing = scenario.Command(new DeleteDocument(scenario.Missing.Collection, scenario.Missing.Id, 1));
        await mcp.ErrorAsync(IsolatedKeyLoadPublicRegressionProtocol.Commit, deleteMissing, ErrorCode.NotFound, token);
        await Assert.That(await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.GetAsync(scenario.Missing, token))).IsNull();
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.GetAsync(scenario.Document, token)))!,
            scenario.Document, scenario.UpdatedStoredJson, 2);
    }

    private static async Task VerifyDeleteAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicRegressionMcp mcp,
        IsolatedKeyLoadPublicRegressionScenario scenario, CancellationToken token)
    {
        var delete = scenario.Command(new DeleteDocument(scenario.Document.Collection, scenario.Document.Id, 2));
        var receipt = await IsolatedKeyLoadPublicRegressionProtocol.SqlAsync<CommitReceipt>(sdk,
            IsolatedKeyLoadPublicRegressionProtocol.Call(scenario.Partition, IsolatedKeyLoadPublicRegressionProtocol.Commit, delete), token);
        await IsolatedKeyLoadPublicRegressionAssertions.ReceiptAsync(receipt, delete.CommandId, 1, scenario.Partition);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(DeleteKind, scenario.Document.Collection, scenario.Document.Id, 3));
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(receipt,
            await mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicRegressionProtocol.Commit, delete, token));
        await Assert.That(await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.GetAsync(scenario.Document, token))).IsNull();
        await Assert.That(await mcp.SuccessAsync<DocumentResult?>(IsolatedKeyLoadPublicRegressionProtocol.Get,
            new GetDocumentRequest(scenario.Document), token)).IsNull();
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.GetAsync(scenario.Sentinel, token)))!,
            scenario.Sentinel, scenario.SentinelJson, 1);
    }
}
