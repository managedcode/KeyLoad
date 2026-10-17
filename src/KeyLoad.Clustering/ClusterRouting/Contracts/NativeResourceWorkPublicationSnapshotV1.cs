namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeResourceWorkSnapshotProtocol.Alias)]
internal sealed record NativeResourceWorkPublicationSnapshotV1(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] int Kind,
    [property: global::Orleans.Id(2)] long AdmissionGeneration,
    [property: global::Orleans.Id(3)] long LastPublicationGeneration);
