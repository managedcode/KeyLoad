namespace KeyLoad.Clustering;

[global::Orleans.Alias(AnnProjectionObservationProtocol.KindAlias)]
internal enum AnnProjectionObservationKind
{
    Identity = 0,
    Wait = 1,
    Approximate = 2
}
