using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Search;

internal static class EventProjectionClusterAssertions
{
    internal static async Task VerifyVectorBothAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        EventProjectionClusterScenario scenario, string readerSecret, (string Id, double Score)[] expected,
        CancellationToken cancellationToken)
    {
        var request = scenario.VectorSearch();
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, cancellationToken));
        var response = await mcp.CallAsync(McpCallerTools.SearchExecute, request, cancellationToken);
        var official = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(response);
        var literal = expected.Select(hit => new RankedDocument(
            new(scenario.StoredReference(hit.Id),
                EventProjectionClusterScenario.Revision, ExpectedDocumentJson(hit.Id), true,
                [EventProjectionClusterScenario.InputField, EventProjectionClusterScenario.VectorField]),
            hit.Score)).ToArray();
        var literalBytes = JsonDefaults.Serialize(literal);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(literalBytes)).IsTrue();
        await Assert.That(JsonDefaults.Serialize(official.Value).AsSpan().SequenceEqual(literalBytes)).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(response, readerSecret, EventProjectionClusterScenario.Secret);
    }

    private const string DerivedJson = "{\"kind\":\"derived\"}";
    private const string BaselineJson = "{\"kind\":\"baseline\"}";
    private const string InvalidExpectedDocument = "The lineage oracle contains an unsupported document identity.";

    private static string ExpectedDocumentJson(string id)
        => id switch
        {
            EventProjectionClusterScenario.TargetId => DerivedJson,
            EventProjectionClusterScenario.BaselineId => BaselineJson,
            _ => throw new InvalidOperationException(InvalidExpectedDocument)
        };

    internal static async Task AssertReceiptAsync(CommitReceipt receipt, EventProjectionClusterScenario scenario)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(scenario.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(receipt.Mutations[0].Kind).IsEqualTo(EventProjectionClusterScenario.MutationKind);
        await Assert.That(receipt.Mutations[0].Resource).IsEqualTo(EventProjectionClusterScenario.Collection);
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(EventProjectionClusterScenario.TargetId);
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(EventProjectionClusterScenario.Revision);
    }

    internal static async Task AssertSdkFailureAsync<T>(Result<T> result, ErrorCode code)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
    }
}
