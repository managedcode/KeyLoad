using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal sealed class ResourceRetirementParentSnapshot
{
    private const int ComponentCount = 6;
    private const int TenantIndex = 1;
    private const int DatabaseIndex = 2;
    private const int DomainIndex = 3;
    private const int PartitionIndex = 4;
    private const int TailIndex = 5;
    private readonly HashSet<(PartitionRef Partition, Guid Move)> active = [];
    private readonly Dictionary<(PartitionRef Partition, Guid Move), bool> headers = [];

    internal static void Capture(ResourceRetirementMetadataScan scan, Action<PartitionRef, Guid> add)
    {
        var capture = new ResourceRetirementParentSnapshot();
        scan.Visit(PartitionMoveParentKeys.HeaderPrefix(), capture.Read);
        foreach (var item in capture.active)
        {
            if (!capture.headers.TryGetValue(item, out var terminal) || terminal)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            add(item.Partition, item.Move);
        }
        if (capture.headers.Any(pair => !pair.Value && !capture.active.Contains(pair.Key)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    private bool Read(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var parts = KeyCodec.Decode(key);
        if (parts.Length != ComponentCount || parts[TenantIndex] is not string tenant
            || parts[DatabaseIndex] is not string database || parts[DomainIndex] is not string domain
            || parts[PartitionIndex] is not string partitionKey)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var partition = new PartitionRef(tenant, database, domain, partitionKey);
        DatabaseEngine.ValidatePartition(partition);
        if (PartitionMoveParentKeys.IsActiveMarker(parts[TailIndex]))
        {
            ResourceRetirementMetadataScan.Exact(key, PartitionMoveParentKeys.Active(partition));
            var moveId = NativeSerialization.Deserialize<Guid>(value);
            if (moveId == Guid.Empty || !active.Add((partition, moveId)))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            return true;
        }
        if (parts[TailIndex] is not Guid move)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var header = NativeSerialization.Deserialize<PartitionMoveParentHeader>(value);
        PartitionMoveParentValidation.RequireHeader(header, partition, move);
        ResourceRetirementMetadataScan.Exact(key, PartitionMoveParentKeys.Header(partition, move));
        headers.Add((partition, move), header.TerminalResult is not null);
        return true;
    }
}
