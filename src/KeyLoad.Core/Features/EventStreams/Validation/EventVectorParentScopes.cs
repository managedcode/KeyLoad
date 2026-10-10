using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorParentScopes
{
    private const int EqualScopeOrder = 0;
    private const string InvalidScopes = "The original event vector source authorization scopes are inconsistent.";

    internal static ImmutableArray<EventVectorAuthorizationScope> Derive(EventVectorControlPhase phase,
        EventVectorOwnedInventory? inventory, EventVectorAdmissionPolicy admission)
    {
        var scopes = new SortedDictionary<string, EventVectorAuthorizationScope>(StringComparer.Ordinal);
        var witness = EventVectorCoverageWitnessEncoding.Read(phase.OriginalCoverageWitness,
            phase.OriginalRequest, admission);
        var coverage = NativeSerialization.Deserialize<EventVectorCoverageSemanticIdentity>(
            witness.OriginalSemanticProjectionBytes.Span);
        EventVectorParentCaptureScopes.Add(scopes, coverage, phase.OriginalRequest.Scope, admission);
        AddEntries(scopes, coverage.OriginalEncodedEntries,
            EventVectorEntryEncoding.Digest(coverage.OriginalEncodedEntries), admission);
        if (!phase.OriginalEncodedEntries.IsEmpty)
        { AddEntries(scopes, phase.OriginalEncodedEntries, phase.OriginalEntryChecksum, admission); }
        if (inventory?.Header is not null)
        {
            foreach (var entry in EventVectorControlPages.Entries(inventory, admission))
            { Add(scopes, entry.Source, admission); }
        }
        if (!phase.OriginalCleanupFrontierBytes.IsEmpty)
        {
            if (inventory?.Header is { } map)
            {
                var key = EventVectorKeys.ParentCall(map.ControlPartition, map.MapId);
                var row = inventory.PayloadRows.SingleOrDefault(current => current.Key.Span.SequenceEqual(key));
                if (row.Key.IsEmpty)
                { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidScopes); }
                var parent = EventVectorParentValidation.Read(map, row, admission);
                foreach (var scope in parent.OriginalAuthorizedSourceScopes)
                { AddPartition(scopes, scope.Partition, scope.Resource, scope.Kind, admission); }
            }
            admission.RequireEncodedBytes(phase.OriginalCleanupFrontierBytes.Length);
            var frontier = NativeSerialization.Deserialize<ImmutableArray<EventVectorCleanupPin>>(
                phase.OriginalCleanupFrontierBytes.Span);
            if (frontier.IsDefault)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidScopes); }
            admission.RequireEntryCount(frontier.Length);
            foreach (var pin in frontier)
            {
                if (pin is null || pin.OriginalPinPhaseBytes.IsEmpty)
                { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidScopes); }
                admission.RequireEncodedBytes(pin.OriginalPinPhaseBytes.Length);
                var original = NativeSerialization.Deserialize<EventVectorSourcePhase>(pin.OriginalPinPhaseBytes.Span);
                Add(scopes, original.Source, admission);
            }
        }
        if (phase.IntendedSourcePhase is { } intended)
        { Add(scopes, intended.Source, admission); }
        if (phase.ObservedSourcePhase is { } observed)
        { Add(scopes, observed.Source, admission); }
        return scopes.Values.ToImmutableArray();
    }

    internal static void Authorize(IAuthorizationPolicy authorization, PrincipalRecord principal,
        ImmutableArray<EventVectorAuthorizationScope> scopes, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (scopes.IsDefault)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidScopes); }
        admission.RequireEntryCount(scopes.Length);
        string? previous = null;
        foreach (var scope in scopes)
        {
            if (scope is null || scope.Partition is null || string.IsNullOrWhiteSpace(scope.Resource)
                || !Enum.IsDefined(scope.Kind))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidScopes); }
            var key = Key(scope);
            if (previous is not null && StringComparer.Ordinal.Compare(previous, key) >= EqualScopeOrder)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidScopes); }
            authorization.Require(principal, scope.Partition, scope.Resource, Capability.SubscriptionsManage);
            authorization.Require(principal, scope.Partition, scope.Resource,
                scope.Kind == EventSourceKind.Stream ? Capability.EventsRead : Capability.TopicsRead);
            previous = key;
        }
    }

    internal static void RequireExact(ImmutableArray<EventVectorAuthorizationScope> original,
        ImmutableArray<EventVectorAuthorizationScope> derived)
    {
        if (original.IsDefault || derived.IsDefault || !original.SequenceEqual(derived))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidScopes); }
    }

    private static void AddEntries(SortedDictionary<string, EventVectorAuthorizationScope> scopes,
        ReadOnlyMemory<byte> entries, ReadOnlyMemory<byte> checksum, EventVectorAdmissionPolicy admission)
    {
        foreach (var entry in EventVectorEntryEncoding.Decode(entries, checksum, admission))
        { Add(scopes, entry.Source, admission); }
    }

    private static void Add(SortedDictionary<string, EventVectorAuthorizationScope> scopes,
        EventSourceRef source, EventVectorAdmissionPolicy admission)
    {
        AddPartition(scopes, source.Partition, source.Resource, source.Kind, admission);
    }

    internal static void AddPartition(SortedDictionary<string, EventVectorAuthorizationScope> scopes,
        PartitionRef partition, string resource, EventSourceKind kind, EventVectorAdmissionPolicy admission)
    {
        var scope = new EventVectorAuthorizationScope(EventVectorIdentityInputs.Partition(partition),
            EventVectorIdentityInputs.Text(resource), kind);
        scopes.TryAdd(Key(scope), scope);
        admission.RequireEntryCount(scopes.Count);
    }

    private static string Key(EventVectorAuthorizationScope scope)
        => Convert.ToHexString(KeyCodec.Encode(scope.Partition.TenantId, scope.Partition.DatabaseId,
            scope.Partition.TransactionDomainId, scope.Partition.PartitionKey, scope.Resource, (int)scope.Kind));
}
