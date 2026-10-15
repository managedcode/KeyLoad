using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class SharedDomainPublicRefusals
{
    internal static async Task ProducerAsync(ClusterFixture fixture, SharedDomainPublicState state, CancellationToken token)
    {
        var first = state.Scopes[FirstIndex];
        var second = state.Scopes[SecondIndex];
        var isolated = state.Scopes[DocumentScopeIndex];
        CommandRequest[] refused =
        [new(Guid.NewGuid(), first.Partition,
            [new PutDocument(first.Documents!, Refused, first.Payload, NoRevision),
             new AppendEvents(first.Streams!, Refused, [new(Refused, EventType, first.Payload, Headers)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(second.Queue!, Refused, first.Payload, Headers)]),
         new(Guid.NewGuid(), isolated.Partition,
            [new PutDocument(isolated.Documents!, Refused, isolated.Payload, NoRevision),
             new AppendEvents(state.Scopes[EventScopeIndex].Streams!, Refused,
                 [new(Refused, EventType, isolated.Payload, Headers)], ExpectedStreamRevision.NoStream)])];
        await SharedDomainPublicSession.RunAsync(fixture, state.Identity.Secret, async (sdk, official) =>
        {
            foreach (var command in refused)
            {
                await EventAppendRf3Assertions.RejectedAsync(sdk, official, command, ErrorCode.Conflict,
                    DomainDetail, state.Identity.Secret, token);
            }
        }, token);
        await SharedDomainPublicModels.AllAsync(fixture, state, token);
    }

    internal static async Task DeniedAsync(KeyLoadClient sdk, McpOfficialClient official, CommandRequest command, CancellationToken token)
    {
        var result = await sdk.CommitAsync(command, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(McpCallerTools.DocumentsCommit, command, token),
            ErrorCode.PermissionDenied, dispatched: true);
    }

    internal static async Task BindingAsync(ClusterFixture fixture, SharedDomainPublicState state, CancellationToken token)
    {
        await SharedDomainPublicSession.RunAsync(fixture, fixture.AdminKey, async (sdk, official) =>
        {
            foreach (var resource in state.Scopes.SelectMany(scope => scope.Resources))
            {
                var changed = resource with
                {
                    TransactionDomainId = resource.TransactionDomainId == FirstDomain ? SecondDomain : FirstDomain,
                    SchemaVersion = SecondRevision
                };
                var id = Guid.NewGuid();
                var request = new ConfigureResourceRequest(state.Identity.Principal.TenantId, Database, changed)
                {
                    ExpectedSchemaVersion = FirstRevision
                };
                var result = await sdk.ConfigureResourceAsync(id, request, token);
                await Assert.That(result.IsFailed).IsTrue();
                await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.UnsupportedCapability.ToString());
                await Assert.That(result.Problem.Detail).IsEqualTo(BindingDetail);
                var arguments = McpOfficialClient.Arguments(request);
                arguments[McpCallerProtocol.CommandId] = id;
                await McpCallerAssertions.ErrorAsync(await official.Client.InvokeKeyLoadToolAsync(
                    McpCallerTools.ResourcesConfigure, arguments, cancellationToken: token), ErrorCode.UnsupportedCapability, dispatched: true);
            }
        }, token);
        await SharedDomainPublicModels.AllAsync(fixture, state, token);
    }
}
