namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaAliases.Schedule)]
internal sealed record RecurringScheduleRecord(
    [property: global::Orleans.Id(RecurringSagaFields.Lane)] QueueLaneRef Lane,
    [property: global::Orleans.Id(RecurringSagaFields.Identity)] Guid ScheduleId,
    [property: global::Orleans.Id(RecurringSagaFields.CreatorPrincipalId)] string CreatorPrincipalId,
    [property: global::Orleans.Id(RecurringSagaFields.Definition)] RecurringScheduleDefinition Definition,
    [property: global::Orleans.Id(RecurringSagaFields.Revision)] long Revision,
    [property: global::Orleans.Id(RecurringSagaFields.Generation)] long Generation,
    [property: global::Orleans.Id(RecurringSagaFields.NextOrdinal)] long NextOrdinal,
    [property: global::Orleans.Id(RecurringSagaFields.Cancelled)] bool Cancelled);
