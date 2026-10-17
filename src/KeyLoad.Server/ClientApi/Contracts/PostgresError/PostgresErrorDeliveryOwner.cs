namespace KeyLoad.Server;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PostgresErrorDeliveryProtocol.OwnerAlias)]
internal sealed record PostgresErrorDeliveryOwner(
    [property: global::Orleans.Id(0)] Guid Session,
    [property: global::Orleans.Id(1)] string Voter);
