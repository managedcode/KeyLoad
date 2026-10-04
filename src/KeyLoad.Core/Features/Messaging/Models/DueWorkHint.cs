namespace KeyLoad.Core.Features.Messaging;

internal static class DueWorkAliases
{
    internal const string Hint = "keyload.core.messaging.due-work-hint.v1";
    internal const string Kind = "keyload.core.messaging.due-work-kind.v1";
}

internal static class DueWorkFields
{
    internal const int Kind = 0;
    internal const int Lane = 1;
    internal const int Id = 2;
    internal const int Creator = 3;
    internal const int Revision = 4;
    internal const int Generation = 5;
    internal const int Ordinal = 6;
    internal const int DueAt = 7;
    internal const string UtcZone = "UTC";
    internal const string GuidFormat = "N";
    internal const long MinimumIntervalTicks = TimeSpan.TicksPerSecond;
    internal const long MaximumIntervalTicks = 365L * TimeSpan.TicksPerDay;
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(DueWorkAliases.Kind)]
internal enum DueWorkKind
{
    Schedule = 0,
    Saga = 1
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(DueWorkAliases.Hint)]
internal sealed record DueWorkHint(
    [property: global::Orleans.Id(DueWorkFields.Kind)] DueWorkKind Kind,
    [property: global::Orleans.Id(DueWorkFields.Lane)] QueueLaneRef Lane,
    [property: global::Orleans.Id(DueWorkFields.Id)] Guid Id,
    [property: global::Orleans.Id(DueWorkFields.Creator)] string CreatorPrincipalId,
    [property: global::Orleans.Id(DueWorkFields.Revision)] long Revision,
    [property: global::Orleans.Id(DueWorkFields.Generation)] long Generation,
    [property: global::Orleans.Id(DueWorkFields.Ordinal)] long Ordinal,
    [property: global::Orleans.Id(DueWorkFields.DueAt)] DateTimeOffset DueAt);
