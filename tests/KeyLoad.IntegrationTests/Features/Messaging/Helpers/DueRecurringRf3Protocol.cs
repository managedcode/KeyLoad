using System.Globalization;
using KeyLoad.Client;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Independent finite test oracle for autonomous recurrence identities and cadence.</summary>
internal static class DueRecurringRf3Protocol
{
    private const string UtcZone = "UTC";
    private const string OccurrencePrefix = "recurring-";
    private const string GuidFormat = "N";
    private const string HexFormat = "x16";
    internal static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    internal static readonly TimeSpan DueLead = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan ProgressWindow = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    internal static RecurringScheduleDefinition Definition(QueueLaneRef lane, Guid scheduleId,
        DateTimeOffset firstDueAt)
        => new(lane, scheduleId, firstDueAt, Interval, UtcZone, RecurringMisfirePolicy.CatchUp,
            MessagingRf3Scenario.ProtectedPayload, MessagingRf3Scenario.ProtectedHeaders);

    internal static string OccurrenceId(Guid scheduleId, long generation, long ordinal)
        => string.Concat(OccurrencePrefix, scheduleId.ToString(GuidFormat), "-",
            generation.ToString(HexFormat, CultureInfo.InvariantCulture), "-",
            ordinal.ToString(HexFormat, CultureInfo.InvariantCulture));
}
