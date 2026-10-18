namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobLiveListR43State(PartitionRef partition)
{
    internal static string Id(int index) => index switch
    {
        0 => "a-boundary",
        1 => "c-deleted",
        2 => "0-before",
        3 => "b-inserted",
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    internal static byte[] Bytes(int index) => index switch
    {
        0 => [17, 19],
        1 => [23, 29],
        2 => [31, 37],
        3 => [41, 43],
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    internal PartitionRef Partition { get; } = partition;
    internal BlobBoundedRetirementPublicPublication.Published[] Versions { get; } = new BlobBoundedRetirementPublicPublication.Published[4];
    internal int Phase
    {
        get; set;
    }
    internal OutboxStatus Outbox { get; set; } = null!;
    internal DeleteBlobRequest Delete { get; set; } = null!;
    internal BlobCommitResult<BlobMetadata> Deleted { get; set; } = null!;
    internal BlobMetadata[] Visible() => Phase switch
    {
        0 => [Versions[0].Completed.Value, Versions[1].Completed.Value],
        1 => [Versions[2].Completed.Value, Versions[0].Completed.Value, Versions[1].Completed.Value],
        2 => [Versions[2].Completed.Value, Versions[0].Completed.Value, Versions[3].Completed.Value, Versions[1].Completed.Value],
        3 => [Versions[2].Completed.Value, Versions[0].Completed.Value, Versions[3].Completed.Value],
        _ => throw new InvalidOperationException()
    };
}
