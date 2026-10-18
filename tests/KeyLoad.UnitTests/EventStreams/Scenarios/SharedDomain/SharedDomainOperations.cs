using System.Text.Json;
using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainOperations
{
    internal static Command<DatabaseCommand> Operation<T>(OperationKind kind, Guid id, T request, string principal = Writer)
        => new(id, kind, principal, default, JsonSerializer.Serialize(request, JsonDefaults.Options));

    internal static Result<object> Apply(DatabaseEngine database, Command<DatabaseCommand> operation, CancellationToken token)
        => database.ApplyEmbedded(operation, token);

    internal static PrincipalRecord Principal(SharedDomainState state, bool denied)
        => new(Writer, Tenant, denied ? [] : [.. state.Scopes.SelectMany(scope => scope.Resources)
            .Select(resource => new ScopeGrant(Database, resource.Name, Capability.All))], [])
        {
            PolicyEpoch = state.PolicyEpoch
        };

    internal static void Configure(TestDatabase fixture, SharedDomainState state)
    {
        foreach (var scope in state.Scopes)
        {
            foreach (var resource in scope.Resources)
            {
                _ = fixture.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(Tenant, Database, resource))
                    .Get<ResourceDefinition>();
            }
        }
        ConfigurePrincipal(fixture.Database, state, denied: false, CancellationToken.None);
    }

    internal static void ConfigurePrincipal(DatabaseEngine database, SharedDomainState state, bool denied, CancellationToken token)
    {
        var operation = Operation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), new ConfigurePrincipalRequest(Principal(state, denied)), Root);
        _ = Apply(database, operation, token).Get<PrincipalRecord>();
    }
}
