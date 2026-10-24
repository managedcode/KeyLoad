using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using P = KeyLoad.IntegrationTests.Authorization.SchemaVisibleProjectionClusterProtocol;
using S = KeyLoad.IntegrationTests.Authorization.SafeProjectorOperationClusterProtocol;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class SchemaVisibleProjectionClusterAuthority
{
    internal static async Task<ResourceDefinition> ResourceAsync(RequestCqrsClusterCallers root,
        SafeProjectorOperationClusterHistory history, ResourceDefinition previous, bool mixed, CancellationToken token)
    {
        var resource = previous with
        {
            SchemaVersion = mixed ? P.MixedVersion : P.HiddenVersion,
            FieldPolicies = [previous.FieldPolicies[P.First] with { SchemaReadGrant = mixed ? null : P.SchemaGrant },
                previous.FieldPolicies[P.Second] with { SchemaReadGrant = P.SchemaGrant },
                previous.FieldPolicies[P.Third] with { SchemaReadGrant = P.SchemaGrant }]
        };
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(history.State.Partition.TenantId,
                    history.State.Partition.DatabaseId, resource)
                {
                    ExpectedSchemaVersion = previous.SchemaVersion
                }, token)));
        }, token);
        return resource;
    }

    internal static async Task PrincipalAsync(RequestCqrsClusterCallers root, SafeProjectorOperationClusterHistory history,
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
                new(state.Partition.DatabaseId, state.Collection + SchemaVisibleProjectionClusterJoins.EventsSuffix,
                    Capability.Query | Capability.EventsRead),
                new(state.Partition.DatabaseId, state.Collection + SchemaVisibleProjectionClusterJoins.TargetSuffix,
                    Capability.Query | Capability.DocumentsRead)]
        };
        var id = Guid.NewGuid();
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigurePrincipalAsync(id, principal, token)));
        }, token);
        history.PrincipalCommands.Add((id, principal));
    }

    internal static async Task InvalidAsync(RequestCqrsClusterCallers root, SafeProjectorOperationClusterHistory history,
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
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationClusterErrors.SdkAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), request, token),
                ErrorCode.Validation, P.IdentifierDetail);
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var arguments = McpOfficialClient.Arguments(request);
            arguments.Add(McpCallerProtocol.CommandId, Guid.NewGuid());
            await SafeProjectorOperationClusterErrors.McpAsync(await root.Mcp.Client.InvokeKeyLoadToolAsync(
                McpCallerTools.ResourcesConfigure, arguments, token), ErrorCode.Validation, state);
        }, token);
    }
}
