namespace KeyLoad.Clustering;

/// <summary>Closed source-owner capability actions; not exposed in the public catalog.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementAliases.CaptureAction)]
internal enum PartitionMovementCaptureAction
{
    Capture = 1,
    Page = 2,
    Release = 3
}
