using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutBudgetAssertions
{
    private const int OverflowByte = 1;
    private const char PayloadCharacter = 'x';
    private const string DocumentPrefix = "native-cut-budget-";
    private const string Invalid = "The genuine native cut budget check requires its retained original scope and destination owners.";

    internal static async Task RequireAsync(TwoRf3MembershipWave wave, string adminCredential,
        KeyLoadClient administrator, NativeCanonicalModelCutRequest scope, string existingCollection,
        IReadOnlyDictionary<string, ReplicaSiloDiscovery> owners,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> secrets, CancellationToken token)
    {
        NativeCanonicalModelCutCodec.ValidateRequest(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminCredential);
        ArgumentException.ThrowIfNullOrWhiteSpace(existingCollection);
        if (!token.CanBeCanceled || scope.SessionId != wave.QueryControls.SessionId
            || owners.Count != TwoRf3MembershipProtocol.NodeCount || secrets.Count != owners.Count)
        { throw new InvalidDataException(Invalid); }
        var originalArm = NativeCanonicalModelCutOriginalArm.Read(wave.QueryControls, scope.ArmId);
        NativeCanonicalModelCutRetainedArmAssertions.RequireSettledOriginal(wave.QueryControls, scope.ArmId, originalArm);
        var json = JsonSerializer.Serialize(new { probe = new string(PayloadCharacter,
            NativeCanonicalModelCutCodec.MaximumRecordBytes + OverflowByte) });
        await Assert.That(Encoding.UTF8.GetByteCount(json)).IsGreaterThan(NativeCanonicalModelCutCodec.MaximumRecordBytes);
        var id = DocumentPrefix + Guid.NewGuid().ToString("N");
        var command = new CommandRequest(Guid.NewGuid(), scope.DestinationPartition,
            [new PutDocument(existingCollection, id, json)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(owners[TwoRf3MembershipProtocol.Node4].Incarnation);
        var entity = new EntityRef(scope.DestinationPartition, existingCollection, id);
        var read = await McpCallerAssertions.SdkSuccessAsync(await administrator.GetAsync(entity, token));
        await Assert.That(read.Document is not null && read.Document.Json == json).IsTrue();
        await RequirePublicReplayAsync(wave, adminCredential, administrator, command, receipt, entity, read, token);
        foreach (var node in NativeCanonicalModelCutFiles.Nodes)
        {
            await NativeCanonicalModelCutBudgetHttpReply.RequireAsync(wave, node, adminCredential,
                scope with { Nonce = Guid.NewGuid() }, json, token).ConfigureAwait(false);
        }
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var expected = owners[node];
            var actual = await TwoRf3MembershipSignedDiscovery.ReadAsync(wave.Application, node,
                expected.ClusterId, expected.Incarnation, secrets[node], adminCredential, token).ConfigureAwait(false);
            await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        }
        token.ThrowIfCancellationRequested();
    }

    private static async Task RequirePublicReplayAsync(TwoRf3MembershipWave wave, string credential,
        KeyLoadClient administrator, CommandRequest command, CommitReceipt receipt, EntityRef entity,
        DocumentReadResult expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, token)));
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            credential, async callers =>
            {
                var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
                await SqlRf3Protocol.EqualAsync(receipt, replay.Value);
                var document = await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                    await callers.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(entity), token));
                await SqlRf3Protocol.EqualAsync(expected, document.Value);
                return receipt;
            }, token);
    }

}
