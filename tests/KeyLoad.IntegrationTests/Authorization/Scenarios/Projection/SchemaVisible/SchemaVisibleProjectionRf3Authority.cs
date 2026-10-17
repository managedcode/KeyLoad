using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionRf3Protocol;
using S = KeyLoad.IntegrationTests.Features.Authorization.SafeProjectorOperationRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionRf3Authority
{
    internal static async Task<ResourceDefinition> ResourceAsync(RequestCqrsRf3Callers root,
        SafeProjectorOperationRf3History history, ResourceDefinition previous, bool mixed, CancellationToken token)
    {
        var resource = previous with
        {
            SchemaVersion = mixed ? P.MixedVersion : P.HiddenVersion,
            FieldPolicies = [previous.FieldPolicies[P.First] with { SchemaReadGrant = mixed ? null : P.SchemaGrant },
                previous.FieldPolicies[P.Second] with { SchemaReadGrant = P.SchemaGrant },
                previous.FieldPolicies[P.Third] with { SchemaReadGrant = P.SchemaGrant }]
        };
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(history.State.Partition.TenantId,
                    history.State.Partition.DatabaseId, resource)
                {
                    ExpectedSchemaVersion = previous.SchemaVersion
                }, token)));
        }, token);
        return resource;
    }

    internal static async Task PrincipalAsync(RequestCqrsRf3Callers root, SafeProjectorOperationRf3History history,
        long epoch, bool raw, bool schema, CancellationToken token)
    {
        var grants = ImmutableArray.CreateBuilder<string>();
        grants.Add(S.UseGrant);
        if (raw)
        {
            grants.Add(S.ReadGrant);
        }
        if (schema)
        {
            grants.Add(P.SchemaGrant);
        }
        var state = history.State;
        var principal = state.Principal(epoch, grants.ToImmutable()) with
        {
            Grants = [new(state.Partition.DatabaseId, state.Collection,
                Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query | Capability.ChangesRead),
                new(state.Partition.DatabaseId, state.Collection + SchemaVisibleProjectionRf3Joins.EventsSuffix,
                    Capability.Query | Capability.EventsRead),
                new(state.Partition.DatabaseId, state.Collection + SchemaVisibleProjectionRf3Joins.TargetSuffix,
                    Capability.Query | Capability.DocumentsRead)]
        };
        var id = Guid.NewGuid();
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigurePrincipalAsync(id, principal, token)));
        }, token);
        history.PrincipalCommands.Add((id, principal));
    }

    internal static async Task InvalidAsync(RequestCqrsRf3Callers root, SafeProjectorOperationRf3History history,
        CancellationToken token)
    {
        var state = history.State;
        var invalid = state.Resource with
        {
            SchemaVersion = P.HiddenVersion,
            FieldPolicies = [state.Resource.FieldPolicies[P.First] with { SchemaReadGrant = P.EmptyGrant }]
        };
        var request = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, invalid)
        {
            ExpectedSchemaVersion = state.Resource.SchemaVersion
        };
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationRf3Errors.SdkAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), request, token),
                ErrorCode.Validation, P.IdentifierDetail);
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            var arguments = McpOfficialClient.Arguments(request);
            arguments.Add(McpCallerProtocol.CommandId, Guid.NewGuid());
            await SafeProjectorOperationRf3Errors.McpAsync(await root.Mcp.Client.InvokeKeyLoadToolAsync(
                McpCallerTools.ResourcesConfigure, arguments, token), ErrorCode.Validation, state);
        }, token);
    }
}
