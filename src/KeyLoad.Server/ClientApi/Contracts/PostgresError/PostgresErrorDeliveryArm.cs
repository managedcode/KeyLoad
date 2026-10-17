namespace KeyLoad.Server;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PostgresErrorDeliveryProtocol.ArmAlias)]
internal sealed record PostgresErrorDeliveryArm(
    [property: global::Orleans.Id(0)] Guid Session,
    [property: global::Orleans.Id(1)] Guid Id,
    [property: global::Orleans.Id(2)] string Subject);
