namespace KeyLoad;

/// <summary>Remote graph delivery result.</summary>
/// <param name="Destination">Destination.</param>
/// <param name="Revision">Revision.</param>
/// <param name="Deleted">Deleted.</param>
/// <param name="TargetReceipt">Target receipt.</param>
/// <param name="CompletionReceipt">Completion receipt.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphDeliveryResultV1.Alias)]
public sealed record RemoteGraphDeliveryResultV1(
    [property: global::Orleans.Id(0)] EntityRef Destination,
    [property: global::Orleans.Id(1)] long Revision,
    [property: global::Orleans.Id(2)] bool Deleted,
    [property: global::Orleans.Id(3)] CommitReceipt TargetReceipt,
    [property: global::Orleans.Id(4)] CommitReceipt CompletionReceipt)
{
    internal const string Alias = "keyload.contract.remote-graph-delivery-result.v1";
}
