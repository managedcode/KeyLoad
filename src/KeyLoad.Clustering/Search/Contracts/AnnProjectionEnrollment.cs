namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnProjectionEnrollmentProtocol.Alias)]
internal sealed record AnnProjectionEnrollment(
    [property: global::Orleans.Id(0)] AnnMaintenanceRequest InitialRequest,
    [property: global::Orleans.Id(1)] AnnMaintenanceRequest LastRequest,
    [property: global::Orleans.Id(2)] AnnMaintenanceResult LastResult);

internal static class AnnProjectionEnrollmentProtocol
{
    internal const string Alias = "keyload.ann-projection-enrollment.v1";
}
