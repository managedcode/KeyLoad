using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Full public operation flows after genuine native retired A and technical Published B setup.</summary>
internal sealed class ProtectedDocumentRf3Scenario(TwoRf3MembershipWave wave) : IAsyncDisposable
{
    private const string Get = "keyload_documents_get";
    private const string Commit = "keyload_documents_commit";
    private readonly List<HttpClient> connections = [];
    private readonly List<McpOfficialClient> sessions = [];
    private readonly List<(CommandRequest Command, CommitReceipt Receipt)> receipts = [];

    private KeyLoadClient Sdk(string node, string secret)
    {
        var http = McpCallerHttp.Create(wave.Application, node);
        connections.Add(http);
        return new(http, secret, IntegrationClientOptions.Execution());
    }

    private async Task<McpOfficialClient> OfficialAsync(string node, string secret, CancellationToken token)
    {
        var client = await McpOfficialClient.ConnectAsync(wave.Application, node, secret, token).ConfigureAwait(false);
        sessions.Add(client);
        return client;
    }

    internal async Task RunAsync(CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token).ConfigureAwait(false);
        var source = Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        var target = Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        var seed = await ProtectedDocumentRf3Seed.CreateAsync(wave, source, token).ConfigureAwait(false);
        var reader = Sdk(TwoRf3MembershipProtocol.Node2, seed.ReaderSecret);
        var readMcp = await OfficialAsync(TwoRf3MembershipProtocol.Node3, seed.ReaderSecret, token).ConfigureAwait(false);
        var commandMcp = await OfficialAsync(TwoRf3MembershipProtocol.Node2, wave.Profile.AdminKey, token).ConfigureAwait(false);
        await DeniedAsync(source, target, seed, token).ConfigureAwait(false);
        var foreign = await reader.GetAsync(seed.Reference, seed.Original.Token, token).ConfigureAwait(false);
        await Assert.That(foreign.IsSuccess).IsFalse();
        await Assert.That(foreign.Value).IsNull();
        await Assert.That(foreign.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        await McpCallerAssertions.ErrorAsync(await readMcp.CallAsync(Get,
            new GetDocumentRequest(seed.Reference, seed.Original.Token), token), ErrorCode.TokenInvalidated, dispatched: true);
        var foreignQ1 = SqlRf3Protocol.Call(seed.Partition, Get, new GetDocumentRequest(seed.Reference, seed.Original.Token));
        await Assert.That((await reader.ExecuteSqlAsync(foreignQ1, token)).Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        await McpCallerAssertions.ErrorAsync(await readMcp.CallAsync(SqlOperationProtocol.ToolName, foreignQ1, token), ErrorCode.TokenInvalidated, dispatched: true);
        await ReadsAsync(source, target, reader, readMcp, seed, 1, "{\"title\":\"\\u041A\\u0438\\u0457\\u0432 original\"}", token).ConfigureAwait(false);
        await ProtectedDocumentRf3Cancellation.RunAsync(wave, reader, readMcp, seed, useMcp: false, token).ConfigureAwait(false);
        await ReadsAsync(source, target, reader, readMcp, seed, 1, "{\"title\":\"\\u041A\\u0438\\u0457\\u0432 original\"}", token).ConfigureAwait(false);
        await ProtectedDocumentRf3Cancellation.RunAsync(wave, reader, readMcp, seed, useMcp: true, token).ConfigureAwait(false);
        await ReadsAsync(source, target, reader, readMcp, seed, 1, "{\"title\":\"\\u041A\\u0438\\u0457\\u0432 original\"}", token).ConfigureAwait(false);
        var put = new CommandRequest(Guid.NewGuid(), seed.Partition, [new PutDocument(ProtectedDocumentRf3Seed.Collection,
            ProtectedDocumentRf3Seed.Entity, "{\"title\":\"replaced\",\"secret\":\"protected-private-canary\"}", 1, ExplicitReplacement: true)]);
        await CommandAsync(source, target, commandMcp, put, "putDocument", 2, token).ConfigureAwait(false);
        await ProtectedDocumentRf3CommandOutcome.ExecuteAsync(wave, source, target, seed,
            put, receipts[^1].Receipt, token).ConfigureAwait(false);
        await ReadsAsync(source, target, reader, readMcp, seed, 2, "{\"title\":\"replaced\"}", token).ConfigureAwait(false);
        var patch = new CommandRequest(Guid.NewGuid(), seed.Partition, [new PatchDocument(ProtectedDocumentRf3Seed.Collection,
            ProtectedDocumentRf3Seed.Entity, [new("/title", PatchKind.Set, "\"patched\"")], 2)]);
        await CommandAsync(source, target, commandMcp, patch, "patchDocument", 3, token).ConfigureAwait(false);
        await ReadsAsync(source, target, reader, readMcp, seed, 3, "{\"title\":\"patched\"}", token).ConfigureAwait(false);
        var delete = new CommandRequest(Guid.NewGuid(), seed.Partition, [new DeleteDocument(ProtectedDocumentRf3Seed.Collection,
            ProtectedDocumentRf3Seed.Entity, 3)]);
        await CommandAsync(source, target, commandMcp, delete, "deleteDocument", 4, token).ConfigureAwait(false);
        await ReadsAsync(source, target, reader, readMcp, seed, 4, null, token).ConfigureAwait(false);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await wave.RemoteRuntime.KillAsync(node, "protected-doc-all-voter-reopen", token).ConfigureAwait(false);
            await wave.RemoteRuntime.RestartAsync(node, token).ConfigureAwait(false);
            await wave.Application.ResourceNotifications.WaitForResourceHealthyAsync(node, token).ConfigureAwait(false);
        }
        await ReadsAsync(source, target, reader, readMcp, seed, 4, null, token).ConfigureAwait(false);
        foreach (var retained in receipts)
        { await ReplayAsync(source, target, commandMcp, retained.Command, retained.Receipt, token).ConfigureAwait(false); }
    }

    private async Task DeniedAsync(KeyLoadClient source, KeyLoadClient target,
        ProtectedDocumentRf3Seed seed, CancellationToken token)
    {
        var beforeA = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var beforeB = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        var sdk = Sdk(TwoRf3MembershipProtocol.Node3, seed.DeniedSecret);
        var denied = await sdk.GetAsync(seed.Reference, token).ConfigureAwait(false);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var mutation = new CommandRequest(Guid.NewGuid(), seed.Partition,
            [new DeleteDocument(ProtectedDocumentRf3Seed.Collection, ProtectedDocumentRf3Seed.Entity, 1)]);
        var write = await sdk.CommitAsync(mutation, token).ConfigureAwait(false);
        await Assert.That(write.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var mcp = await OfficialAsync(TwoRf3MembershipProtocol.Node3, seed.DeniedSecret, token).ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(Get, new GetDocumentRequest(seed.Reference), token),
            ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(Commit, mutation, token), ErrorCode.PermissionDenied, dispatched: true);
        var q1Read = SqlRf3Protocol.Call(seed.Partition, Get, new GetDocumentRequest(seed.Reference));
        await Assert.That((await sdk.ExecuteSqlAsync(q1Read, token)).Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, q1Read, token), ErrorCode.PermissionDenied, dispatched: true);
        var q1Write = SqlRf3Protocol.Call(seed.Partition, Commit, mutation, mutation.CommandId);
        await Assert.That((await sdk.ExecuteSqlAsync(q1Write, token)).Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, q1Write, token), ErrorCode.PermissionDenied, dispatched: true);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token))).Applied).IsGreaterThanOrEqualTo(beforeA.Applied);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token))).Applied).IsGreaterThanOrEqualTo(beforeB.Applied);
    }

    private static async Task ReadsAsync(KeyLoadClient source, KeyLoadClient target, KeyLoadClient reader,
        McpOfficialClient official, ProtectedDocumentRf3Seed seed, long revision, string? json, CancellationToken token)
    {
        var beforeA = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var beforeB = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        var expected = json is null ? null : new DocumentResult(seed.Reference, revision, json, true, ["/secret"]);
        var request = new GetDocumentRequest(seed.Reference);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await reader.GetAsync(seed.Reference, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentResult?>(await official.CallAsync(Get, request, token))).Value);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<DocumentResult?>(reader, SqlRf3Protocol.Call(seed.Partition, Get, request), token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<DocumentResult?>(official, SqlRf3Protocol.Call(seed.Partition, Get, request), token));
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token))).Applied).IsGreaterThanOrEqualTo(beforeA.Applied);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token))).Applied).IsGreaterThanOrEqualTo(beforeB.Applied);
    }

    private async Task CommandAsync(KeyLoadClient sdk, KeyLoadClient target, McpOfficialClient mcp, CommandRequest command, string kind, long revision, CancellationToken token)
    {
        var beforeTarget = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token).ConfigureAwait(false);
        var receiver = directory.Owners.Single(entry => entry.Owner.PhysicalShardId != directory.ControlOwner.PhysicalShardId).Owner;
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(receiver.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(2L);
        await Assert.That(beforeTarget.Incarnation).IsEqualTo(receiver.Incarnation);
        var afterTarget = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        await Assert.That(afterTarget.Incarnation).IsEqualTo(receiver.Incarnation);
        await Assert.That(receipt.Token.Position).IsGreaterThan(beforeTarget.Applied);
        await Assert.That(receipt.Token.Position).IsLessThanOrEqualTo(afterTarget.Applied);
        await TokenDocumentAsync(sdk, mcp, command, kind, receipt.Token, token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt(kind, ProtectedDocumentRf3Seed.Collection,
            ProtectedDocumentRf3Seed.Entity, revision), receipt.Mutations.Single());
        receipts.Add((command, receipt));
        await ReplayAsync(sdk, target, mcp, command, receipt, token).ConfigureAwait(false);
        var beforeConflict = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        var changed = command with
        {
            Mutations = [new PutDocument(ProtectedDocumentRf3Seed.Collection,
            ProtectedDocumentRf3Seed.Entity, "{\"title\":\"different\"}", 0)]
        };
        await Assert.That((await sdk.CommitAsync(changed, token)).Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(Commit, changed, token), ErrorCode.Conflict, dispatched: true);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token))).Applied).IsGreaterThanOrEqualTo(beforeConflict.Applied);
    }

    private static async Task ReplayAsync(KeyLoadClient sdk, KeyLoadClient target, McpOfficialClient mcp, CommandRequest command,
        CommitReceipt expected, CancellationToken token)
    {
        var beforeTarget = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token));
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(Commit, command, token))).Value);
        var q1 = SqlRf3Protocol.Call(command.Partition, Commit, command, command.CommandId);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, q1, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, q1, token));
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(token))).Applied).IsGreaterThanOrEqualTo(beforeTarget.Applied);
    }

    private static async Task TokenDocumentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, string kind, CommitToken minimum, CancellationToken token)
    {
        var reference = new EntityRef(command.Partition, "protected-documents", "one");
        DocumentResult? expected = kind switch
        {
            "putDocument" => new(reference, 2, "{\"title\":\"replaced\",\"secret\":\"protected-private-canary\"}", false, []),
            "patchDocument" => new(reference, 3, "{\"title\":\"patched\",\"secret\":\"protected-private-canary\"}", false, []),
            "deleteDocument" => null,
            _ => throw new InvalidDataException("The protected document oracle kind is invalid.")
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await sdk.GetAsync(reference, minimum, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentResult?>(
            await mcp.CallAsync(Get, new GetDocumentRequest(reference, minimum), token))).Value);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        foreach (var client in sessions)
        { await ServerFailureObserver.ObserveAsync(() => client.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        foreach (var connection in connections)
        { ServerFailureObserver.Observe(connection.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
