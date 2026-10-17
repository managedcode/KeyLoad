namespace KeyLoad.Replication;

/// <summary>Requests complete image verification and canonical installation.</summary>
/// <param name="LeaderId">Configured voter completing the image.</param>
/// <param name="Term">Sender's current leadership term.</param>
/// <param name="TransferId">Identity of the completed transfer.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.SnapshotCompleteRequest)]
public sealed record SnapshotCompleteRequest(
    [property: Orleans.Id(0)] string LeaderId,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] Guid TransferId);
