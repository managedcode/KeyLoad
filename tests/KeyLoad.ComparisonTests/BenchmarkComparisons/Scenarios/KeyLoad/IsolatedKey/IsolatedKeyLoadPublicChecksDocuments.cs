using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-004/005: genuine callers preserve absent-create, revision CAS, command identity and exact bodies.</summary>
internal static class IsolatedKeyLoadPublicChecksDocuments
{
    private const string PutKind = "putDocument";
    private const string DeleteKind = "deleteDocument";

    internal static async Task VerifyAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicChecksMcp mcp,
        IsolatedKeyLoadPublicChecksScenario scenario, CancellationToken token)
    {
        var create = scenario.Command(new PutDocument(scenario.Document.Collection, scenario.Document.Id, scenario.InitialJson, 0),
            new PutDocument(scenario.Sentinel.Collection, scenario.Sentinel.Id, scenario.SentinelJson, 0));
        var receipt = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await sdk.CommitAsync(create, token));
        await IsolatedKeyLoadPublicChecksAssertions.ReceiptAsync(receipt, create.CommandId, 2, scenario.Partition, scenario.RequiredCopies);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(PutKind, scenario.Document.Collection, scenario.Document.Id, 1));
        await Assert.That(receipt.Mutations[1]).IsEqualTo(new MutationReceipt(PutKind, scenario.Sentinel.Collection, scenario.Sentinel.Id, 1));
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(receipt,
            await mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicChecksProtocol.Commit, create, token));
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(receipt,
            await IsolatedKeyLoadPublicChecksProtocol.SqlAsync<CommitReceipt>(sdk,
                IsolatedKeyLoadPublicChecksProtocol.Call(scenario.Partition, IsolatedKeyLoadPublicChecksProtocol.Commit, create), token));
        var initial = await mcp.RequiredDocumentAsync(IsolatedKeyLoadPublicChecksProtocol.Get,
            new GetDocumentRequest(scenario.Document), token);
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(initial, scenario.Document, scenario.InitialStoredJson, 1);
        await VerifyUpdateAsync(sdk, mcp, scenario, token);
        await VerifyRejectedMutationsAsync(sdk, mcp, scenario, token);
        await VerifyDeleteAsync(sdk, mcp, scenario, token);
    }

    private static async Task VerifyUpdateAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicChecksMcp mcp,
        IsolatedKeyLoadPublicChecksScenario scenario, CancellationToken token)
    {
        var update = scenario.Command(new PutDocument(scenario.Document.Collection, scenario.Document.Id,
            scenario.UpdatedJson, 1, ExplicitReplacement: true));
        var receipt = await mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicChecksProtocol.Commit, update, token);
        await IsolatedKeyLoadPublicChecksAssertions.ReceiptAsync(receipt, update.CommandId, 1, scenario.Partition, scenario.RequiredCopies);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(PutKind, scenario.Document.Collection, scenario.Document.Id, 2));
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(receipt,
            await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await sdk.CommitAsync(update, token)));
        var document = await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await sdk.GetAsync(scenario.Document, token));
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(document!, scenario.Document, scenario.UpdatedStoredJson, 2);
    }

    private static async Task VerifyRejectedMutationsAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicChecksMcp mcp,
        IsolatedKeyLoadPublicChecksScenario scenario, CancellationToken token)
    {
        var duplicate = scenario.Command(new PutDocument(scenario.Document.Collection, scenario.Document.Id, scenario.InitialJson, 0));
        await IsolatedKeyLoadPublicChecksAssertions.ErrorAsync(await sdk.CommitAsync(duplicate, token), ErrorCode.RevisionConflict);
        await mcp.ErrorAsync(IsolatedKeyLoadPublicChecksProtocol.Commit, duplicate, ErrorCode.RevisionConflict, token);
        var updateMissing = scenario.Command(new PutDocument(scenario.Missing.Collection, scenario.Missing.Id,
            scenario.UpdatedJson, 1, ExplicitReplacement: true));
        await IsolatedKeyLoadPublicChecksAssertions.ErrorAsync(await sdk.CommitAsync(updateMissing, token), ErrorCode.RevisionConflict);
        var deleteMissing = scenario.Command(new DeleteDocument(scenario.Missing.Collection, scenario.Missing.Id, 1));
        await mcp.ErrorAsync(IsolatedKeyLoadPublicChecksProtocol.Commit, deleteMissing, ErrorCode.NotFound, token);
        await Assert.That(await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await sdk.GetAsync(scenario.Missing, token))).IsNull();
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await sdk.GetAsync(scenario.Document, token)))!,
            scenario.Document, scenario.UpdatedStoredJson, 2);
    }

    private static async Task VerifyDeleteAsync(KeyLoadClient sdk, IsolatedKeyLoadPublicChecksMcp mcp,
        IsolatedKeyLoadPublicChecksScenario scenario, CancellationToken token)
    {
        var delete = scenario.Command(new DeleteDocument(scenario.Document.Collection, scenario.Document.Id, 2));
        var receipt = await IsolatedKeyLoadPublicChecksProtocol.SqlAsync<CommitReceipt>(sdk,
            IsolatedKeyLoadPublicChecksProtocol.Call(scenario.Partition, IsolatedKeyLoadPublicChecksProtocol.Commit, delete), token);
        await IsolatedKeyLoadPublicChecksAssertions.ReceiptAsync(receipt, delete.CommandId, 1, scenario.Partition, scenario.RequiredCopies);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(DeleteKind, scenario.Document.Collection, scenario.Document.Id, 3));
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(receipt,
            await mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicChecksProtocol.Commit, delete, token));
        await Assert.That(await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await sdk.GetAsync(scenario.Document, token))).IsNull();
        await Assert.That(await mcp.DocumentAsync(IsolatedKeyLoadPublicChecksProtocol.Get,
            new GetDocumentRequest(scenario.Document), token)).IsNull();
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await sdk.GetAsync(scenario.Sentinel, token)))!,
            scenario.Sentinel, scenario.SentinelJson, 1);
    }
}
