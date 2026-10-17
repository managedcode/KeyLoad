namespace KeyLoad.Replication;

/// <summary>Starts or resumes a fenced incoming canonical image.</summary>
/// <param name="LeaderId">Configured voter sending the image.</param>
/// <param name="Term">Sender's current leadership term.</param>
/// <param name="Snapshot">Complete expected image and cut description.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.SnapshotBeginRequest)]
public sealed record SnapshotBeginRequest(
    [property: Orleans.Id(0)] string LeaderId,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] ReplicaSnapshot Snapshot);
