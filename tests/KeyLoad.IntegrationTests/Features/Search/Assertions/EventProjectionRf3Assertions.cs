using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class EventProjectionRf3Assertions
{
    internal static async Task VerifyVectorBothAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SearchRequest request, string readerSecret, (string Id, double Score)[] expected,
        CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, cancellationToken));
        var response = await mcp.CallAsync(McpCallerTools.SearchExecute, request, cancellationToken);
        var official = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(response);
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Document.Reference.Id).IsEqualTo(expected[index].Id);
            await Assert.That(actual[index].Score).IsEqualTo(expected[index].Score);
        }
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(official.Value))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(response, readerSecret, EventProjectionRf3Scenario.Secret);
    }

    internal static async Task AssertReceiptAsync(CommitReceipt receipt, EventProjectionRf3Scenario scenario)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(scenario.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(receipt.Mutations[0].Kind).IsEqualTo(EventProjectionRf3Scenario.MutationKind);
        await Assert.That(receipt.Mutations[0].Resource).IsEqualTo(EventProjectionRf3Scenario.Collection);
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(EventProjectionRf3Scenario.TargetId);
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(EventProjectionRf3Scenario.Revision);
    }

    internal static async Task AssertSdkFailureAsync<T>(Result<T> result, ErrorCode code)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
    }
}
