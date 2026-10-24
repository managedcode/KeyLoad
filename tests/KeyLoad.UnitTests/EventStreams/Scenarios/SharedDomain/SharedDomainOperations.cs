using System.Text.Json;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using static KeyLoad.RecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.EventStreams;

internal static class SharedDomainOperations
{
    internal static Command<DatabaseCommand> Operation<T>(DatabaseEngine database, OperationKind kind, Guid id, T request, string principal = Writer)
        => database.CreateCommand(kind, id, principal, database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));

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
                if (resource.Kind == ResourceKind.Collection)
                {
                    state.DocumentLifetimes.Add((scope.Partition, resource.Name),
                        fixture.ExpectedReference(new(scope.Partition, resource.Name, Item)).Lifetime!);
                }
            }
        }
        ConfigurePrincipal(fixture.Database, state, denied: false, CancellationToken.None);
    }

    internal static void ConfigurePrincipal(DatabaseEngine database, SharedDomainState state, bool denied, CancellationToken token)
    {
        var operation = Operation(database, OperationKind.ConfigurePrincipal, Guid.NewGuid(), new ConfigurePrincipalRequest(Principal(state, denied)), Root);
        _ = Apply(database, operation, token).Get<PrincipalRecord>();
    }
}
