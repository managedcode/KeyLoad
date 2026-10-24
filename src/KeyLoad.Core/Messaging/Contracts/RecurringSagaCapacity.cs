namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaAliases.Capacity)]
internal sealed record RecurringSagaCapacity(
    [property: global::Orleans.Id(RecurringSagaFields.Records)] long Records,
    [property: global::Orleans.Id(RecurringSagaFields.Bytes)] long Bytes);
