using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Proves complete public replay, conflict and healthy continuation after an actual interruption.</summary>
internal static class RequestCqrsFaultReplayContinuation
{
    private const string ConflictDetail = "The command ID was already used with different content.";
    private const string PutKind = "putDocument";
    private const int OneMutation = 1;
    private const int MutationIndex = 0;
    private const int PlacementVersion = 1;
    private const long ZeroPosition = 0;

    internal static async Task VerifyAsync(RequestCqrsRf3Callers callers, KeyLoadClient administrator, EntityRef reference, CommandRequest original,
        CommitReceipt originalReceipt, string healthyJson, long committedRevision, string committedJson,
        CancellationToken cancellationToken)
    {
        var changed = original with
        {
            Mutations = [new PutDocument(reference.Collection, reference.Id, healthyJson,
                ExpectedRevision: committedRevision, ExplicitReplacement: true)]
        };
        var rejection = await RequestCqrsFaultCallers.CommitSdkAsync(callers.Sdk, changed, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(rejection.IsSuccess).IsFalse();
        await Assert.That(rejection.Value).IsNull();
        await Assert.That(rejection.Problem?.ErrorCode).IsEqualTo(ErrorCode.Conflict.ToString());
        await Assert.That(rejection.Problem?.Detail).IsEqualTo(ConflictDetail);
        var mcpRejection = await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, changed,
            cancellationToken).ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(mcpRejection, ErrorCode.Conflict, dispatched: true).ConfigureAwait(false);
        await Assert.That(mcpRejection.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(ConflictDetail);
        await DocumentAsync(callers, reference, committedRevision, committedJson, cancellationToken)
            .ConfigureAwait(false);
        await ReplayAsync(callers, original, originalReceipt, cancellationToken).ConfigureAwait(false);

        await HealthyContinuationAsync(callers, administrator, reference, original, originalReceipt,
            changed, committedRevision, healthyJson, cancellationToken).ConfigureAwait(false);
    }

    private static async Task HealthyContinuationAsync(RequestCqrsRf3Callers callers, KeyLoadClient administrator,
        EntityRef reference, CommandRequest original, CommitReceipt originalReceipt, CommandRequest changed,
        long committedRevision, string healthyJson, CancellationToken cancellationToken)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadAtomicPartitionPlacementAsync(
            new(PlacementVersion, reference.Partition), cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(placement.Partition).IsEqualTo(reference.Partition);
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await callers.AssertFreshRoutingReadyAsync(cancellationToken).ConfigureAwait(false);
        await Assert.That(before.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(originalReceipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(originalReceipt.Token.AtomicPartitionId).IsEqualTo(reference.Partition.AtomicPartitionId);
        await Assert.That(originalReceipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(originalReceipt.Token.Position).IsGreaterThan(ZeroPosition);
        await Assert.That(originalReceipt.Token.Position).IsLessThanOrEqualTo(before.Applied);
        await Assert.That(originalReceipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var healthy = changed with { CommandId = Guid.NewGuid() };
        await Assert.That(healthy.CommandId).IsNotEqualTo(original.CommandId);
        var healthyReceipt = await McpCallerAssertions.SdkSuccessAsync(await RequestCqrsFaultCallers.CommitSdkAsync(
            callers.Sdk, healthy, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var healthyRevision = checked(committedRevision + 1);
        await Assert.That(healthyReceipt.CommandId).IsEqualTo(healthy.CommandId);
        await Assert.That(healthyReceipt.Mutations.Length).IsEqualTo(OneMutation);
        var mutation = new MutationReceipt(PutKind, reference.Collection, reference.Id, healthyRevision);
        await Assert.That(JsonDefaults.Serialize(healthyReceipt.Mutations[MutationIndex])
            .SequenceEqual(JsonDefaults.Serialize(mutation))).IsTrue();
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await callers.AssertFreshRoutingReadyAsync(cancellationToken).ConfigureAwait(false);
        await Assert.That(after.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        var currentPlacement = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadAtomicPartitionPlacementAsync(
            new(PlacementVersion, reference.Partition), cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(currentPlacement).SequenceEqual(JsonDefaults.Serialize(placement))).IsTrue();
        await Assert.That(healthyReceipt.Token.Position).IsGreaterThan(before.Applied);
        await Assert.That(healthyReceipt.Token.Position).IsGreaterThan(originalReceipt.Token.Position);
        await Assert.That(healthyReceipt.Token.Position).IsLessThanOrEqualTo(after.Applied);
        var expectedReceipt = new CommitReceipt(healthy.CommandId,
            new(placement.Incarnation, reference.Partition.AtomicPartitionId,
                healthyReceipt.Token.Position, placement.PlacementEpoch), [mutation], DurabilityProfile.QuorumProcessDurable);
        await Assert.That(JsonDefaults.Serialize(healthyReceipt).SequenceEqual(JsonDefaults.Serialize(expectedReceipt))).IsTrue();
        await ReplayAsync(callers, healthy, healthyReceipt, cancellationToken).ConfigureAwait(false);
        await DocumentAtMinimumAsync(callers, reference, healthyReceipt.Token, healthyRevision, healthyJson,
            cancellationToken).ConfigureAwait(false);
        await DocumentAsync(callers, reference, healthyRevision, healthyJson, cancellationToken).ConfigureAwait(false);
        await ReplayAsync(callers, original, originalReceipt, cancellationToken).ConfigureAwait(false);
        await DocumentAsync(callers, reference, healthyRevision, healthyJson, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task DocumentAsync(RequestCqrsRf3Callers callers, EntityRef reference, long revision,
        string json, CancellationToken cancellationToken)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetAsync(reference, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        var expected = new DocumentResult(reference, revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(sdk).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(mcp.Value).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static async Task DocumentAtMinimumAsync(RequestCqrsRf3Callers callers, EntityRef reference,
        CommitToken minimum, long revision, string json, CancellationToken cancellationToken)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetAsync(reference, minimum,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum), cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var expected = new DocumentResult(reference, revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(sdk).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(mcp.Value).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static async Task ReplayAsync(RequestCqrsRf3Callers callers, CommandRequest command,
        CommitReceipt originalReceipt, CancellationToken cancellationToken)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await RequestCqrsFaultCallers.CommitSdkAsync(callers.Sdk,
            command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(sdk).SequenceEqual(JsonDefaults.Serialize(originalReceipt))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(mcp.Value).SequenceEqual(JsonDefaults.Serialize(originalReceipt))).IsTrue();
    }
}
