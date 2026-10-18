namespace KeyLoad.Core.Features.Messaging;

/// <summary>Carries bounded original due-work identity without its retained business payload.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(DueWorkAliases.Hint)]
public sealed record DueWorkHint(
    [property: global::Orleans.Id(DueWorkFields.Kind)] DueWorkKind Kind,
    [property: global::Orleans.Id(DueWorkFields.Lane)] QueueLaneRef Lane,
    [property: global::Orleans.Id(DueWorkFields.Id)] Guid Id,
    [property: global::Orleans.Id(DueWorkFields.Creator)] string CreatorPrincipalId,
    [property: global::Orleans.Id(DueWorkFields.Revision)] long Revision,
    [property: global::Orleans.Id(DueWorkFields.Generation)] long Generation,
    [property: global::Orleans.Id(DueWorkFields.Ordinal)] long Ordinal,
    [property: global::Orleans.Id(DueWorkFields.DueAt)] DateTimeOffset DueAt);
