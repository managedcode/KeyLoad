using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateEntityReferences
{
    internal static async Task<EntityRef> CaptureAsync(FunctionalGateContext context,
        RequestCqrsRf3Callers caller, DocumentResult literalExpected, CancellationToken token)
    {
        var actual = await ReadAsync(context, caller, literalExpected, token).ConfigureAwait(false);
        context.ReferenceBindings.Capture(actual);
        return actual;
    }

    internal static async Task<EntityRef> CaptureRecreatedAsync(FunctionalGateContext context,
        RequestCqrsRf3Callers caller, EntityRef originalBound, DocumentResult freshLiteral, CancellationToken token)
    {
        await Assert.That(originalBound.Lifetime).IsNotNull();
        await Assert.That(freshLiteral.Reference.Lifetime).IsNull();
        var actual = await ReadAsync(context, caller, freshLiteral, token).ConfigureAwait(false);
        await Assert.That(actual.Lifetime).IsNotEqualTo(originalBound.Lifetime);
        context.ReferenceBindings.ReplaceRecreated(originalBound, actual);
        return actual;
    }

    private static async Task<EntityRef> ReadAsync(FunctionalGateContext context,
        RequestCqrsRf3Callers caller, DocumentResult expected, CancellationToken token)
    {
        var sdk = await context.SdkAsync(caller.Sdk.GetAsync(expected.Reference, token)).ConfigureAwait(false);
        await Assert.That(sdk.Document).IsNotNull();
        var actual = sdk.Document!;
        var reference = actual.Reference;
        await SqlRf3Protocol.EqualAsync(expected.Reference.Partition, reference.Partition);
        await Assert.That(reference.Collection).IsEqualTo(expected.Reference.Collection);
        await Assert.That(reference.Id).IsEqualTo(expected.Reference.Id);
        await Assert.That(reference.Lifetime).IsNotNull();
        await Assert.That(string.IsNullOrWhiteSpace(reference.Lifetime!.Incarnation)).IsFalse();
        if (expected.Reference.Lifetime is { } supplied)
        {
            await SqlRf3Protocol.EqualAsync(supplied, reference.Lifetime);
        }
        // The server-issued lifetime is the only unknown; all row fields remain independent literals.
        await SqlRf3Protocol.EqualAsync(expected with
        {
            Reference = reference
        }, actual);
        var official = await context.McpAsync<DocumentReadResult>(caller, McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(sdk, official);
        return reference;
    }
}
