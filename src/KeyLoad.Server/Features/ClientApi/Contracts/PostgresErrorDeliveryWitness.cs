namespace KeyLoad.Server;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PostgresErrorDeliveryProtocol.WitnessAlias)]
internal sealed record PostgresErrorDeliveryWitness(
    [property: global::Orleans.Id(0)] Guid Session,
    [property: global::Orleans.Id(1)] Guid Arm,
    [property: global::Orleans.Id(2)] Guid Connection,
    [property: global::Orleans.Id(3)] PostgresErrorDeliveryPhase Phase,
    [property: global::Orleans.Id(4)] bool PrimaryFirst,
    [property: global::Orleans.Id(5)] ErrorCode Primary,
    [property: global::Orleans.Id(6)] int Failures);
