namespace KeyLoad.Client.Features.ClusterRouting;

internal static class PhysicalShardPartitionPlanAdmission
{
    private const int MaximumPartitions = 64;
    private const int MinimumPartitions = 1;
    private const int InitialIndex = 0;
    private const long MinimumPlacementRevision = 0;
    private const string InvalidPlan = "A bounded ordered plan requires distinct whole partitions, stable movement IDs, original CAS and one destination.";

    internal static PartitionMoveRequest[] Snapshot(IReadOnlyList<PartitionMoveRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var count = requests.Count;
        if (count is < MinimumPartitions or > MaximumPartitions)
        {
            throw new ArgumentOutOfRangeException(nameof(requests), InvalidPlan);
        }
        var original = new PartitionMoveRequest[count];
        var movements = new HashSet<Guid>();
        var partitions = new HashSet<PartitionRef>();
        Guid? destination = null;
        for (var index = InitialIndex; index < count; index++)
        {
            var request = requests[index];
            if (request is null || request.MoveId == Guid.Empty
                || request.DestinationPhysicalShardId == Guid.Empty || request.ExpectedPlacementRevision < MinimumPlacementRevision
                || request.Mode is not (PartitionMoveMode.Transfer or PartitionMoveMode.Resume)
                || request.Partition is not { } partition
                || string.IsNullOrEmpty(partition.TenantId) || string.IsNullOrEmpty(partition.DatabaseId)
                || string.IsNullOrEmpty(partition.TransactionDomainId) || string.IsNullOrEmpty(partition.PartitionKey)
                || !movements.Add(request.MoveId) || !partitions.Add(partition)
                || destination is { } previous && previous != request.DestinationPhysicalShardId)
            {
                throw new ArgumentException(InvalidPlan, nameof(requests));
            }
            destination = request.DestinationPhysicalShardId;
            original[index] = request;
        }
        return original;
    }
}
