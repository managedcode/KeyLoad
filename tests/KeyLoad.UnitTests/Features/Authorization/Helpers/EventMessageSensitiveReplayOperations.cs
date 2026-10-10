using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventMessageSensitiveReplayOperations
{
    internal static OperationResult Apply(DatabaseEngine database, OperationKind kind, object request,
        string principal, Guid id, CancellationToken token)
        => database.ApplyEmbedded(new(id, kind, principal, default, JsonSerializer.Serialize(request, request.GetType(), JsonDefaults.Options)), token);

    internal static object Receive(EventMessageSensitiveReplayState state, Guid id, bool fresh)
        => state.Subscription
            ? new ReceiveSubscriptionRequest(id, fresh ? new(state.Source, EventMessageSensitiveReplayProtocol.FreshGroup) : state.OriginalGroup, LeaseSeconds: new SubscriptionPolicy().MaxLeaseSeconds)
            : new ReceiveRequest(id, state.Lane, LeaseSeconds: state.Resource.QueuePolicy.MaxLeaseSeconds);

    internal static void ConfigureGroup(DatabaseEngine database, EventMessageSensitiveReplayState state,
        bool fresh, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var group = fresh ? new SubscriptionRef(state.Source, EventMessageSensitiveReplayProtocol.FreshGroup) : state.OriginalGroup;
        Apply(database, OperationKind.ConfigureSubscription, new ConfigureSubscriptionRequest(id, group, new(state.Worker.Id)),
            EventMessageSensitiveReplayProtocol.Root, id, token).Get<SubscriptionInfo>();
    }

    internal static ResourceDefinition ReplacePolicy(DatabaseEngine database, EventMessageSensitiveReplayState state,
        bool required, CancellationToken token)
    {
        var policies = state.Header ? state.Resource.HeaderPolicies : state.Resource.FieldPolicies;
        var replaced = policies.Select(policy => policy with
        { RawReadGrant = EventMessageSensitiveReplayProtocol.GrantAfter, RequiredForProcessing = required }).ToImmutableArray();
        var definition = state.Header ? state.Resource with { HeaderPolicies = replaced } : state.Resource with { FieldPolicies = replaced };
        definition = definition with { SchemaVersion = checked(state.Resource.SchemaVersion + EventMessageSensitiveReplayProtocol.SchemaVersionStep) };
        var request = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, definition)
        { ExpectedSchemaVersion = state.Resource.SchemaVersion };
        return Apply(database, OperationKind.ConfigureResource, request, EventMessageSensitiveReplayProtocol.Root,
            Guid.NewGuid(), token).Get<ResourceDefinition>();
    }

    internal static void Principal(DatabaseEngine database, PrincipalRecord principal, CancellationToken token)
        => Apply(database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal),
            EventMessageSensitiveReplayProtocol.Root, Guid.NewGuid(), token).Get<PrincipalRecord>();

    internal static byte[] Outcome(ZoneTreeStore store, EventMessageSensitiveReplayState state)
        => store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(state.Partition, state.Caller, state.OriginalId))
            ?? throw new InvalidOperationException("The genuine original delivery outcome is absent."));
}
