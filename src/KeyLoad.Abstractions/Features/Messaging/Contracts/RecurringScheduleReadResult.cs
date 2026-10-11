namespace KeyLoad;

/// <summary>Contains the authorized recurring schedule, or successful absence.</summary>
/// <param name="Schedule">The projected schedule; null means no matching schedule exists.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PublicReadResultAliases.Schedule)]
public sealed record RecurringScheduleReadResult([property: Orleans.Id(0)] RecurringScheduleInspection? Schedule);
