using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorCoverageLifetimeValidation
{
    private const string Invalid = "The event vector source lifetime proof is unavailable or inconsistent.";

    internal static EventVectorCoverageCapture Capture(IKeyValueView view,
        EventVectorCoverageCapture capture, EventVectorAdmissionPolicy admission)
    {
        var partition = capture.ControlPartition;
        var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId,
            partition.DatabaseId, capture.Scope.Resource));
        var lifetime = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId,
            capture.Scope.Resource, definition);
        var result = capture with
        {
            SourceDefinition = definition,
            SourceLifetime = lifetime
        };
        _ = Read(result, admission);
        return result;
    }

    internal static ResourceLifetimeV1 Require(EventVectorCoverageWitness witness,
        EventVectorAdmissionPolicy admission)
    {
        ResourceLifetimeV1? selected = null;
        foreach (var packet in witness.OriginalPackets)
        {
            admission.RequireEncodedBytes(packet.OriginalCaptureBytes.Length);
            var capture = NativeSerialization.Deserialize<EventVectorCoverageCapture>(packet.OriginalCaptureBytes.Span);
            var lifetime = Read(capture, admission);
            if (lifetime is null)
            {
                continue;
            }
            if (selected is not null && selected != lifetime)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
            }
            selected = lifetime;
        }
        return selected ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
    }

    private static ResourceLifetimeV1? Read(EventVectorCoverageCapture capture,
        EventVectorAdmissionPolicy admission)
    {
        var definition = capture.SourceDefinition;
        var lifetime = capture.SourceLifetime;
        var entries = EventVectorEntryEncoding.Decode(capture.OriginalEncodedEntries, capture.EntryChecksum, admission);
        if (definition is null && lifetime is null && entries.IsEmpty)
        {
            return null;
        }
        var scope = capture.Scope;
        var partition = capture.ControlPartition;
        var kind = scope.Kind == EventSourceKind.Topic ? ResourceKind.Topic : ResourceKind.StreamSet;
        if (definition is null || lifetime is null || definition.Name != scope.Resource
            || definition.Kind != kind || definition.TransactionDomainId != scope.Domain)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        ResourceLifetimeIdentity.Validate(lifetime, partition.TenantId, partition.DatabaseId, definition);
        foreach (var entry in entries)
        {
            if (entry.Source.Partition.TenantId != lifetime.TenantId
                || entry.Source.Partition.DatabaseId != lifetime.DatabaseId
                || entry.Source.Partition.TransactionDomainId != lifetime.TransactionDomainId
                || entry.Source.Resource != lifetime.ResourceName)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
            }
        }
        return lifetime;
    }
}
