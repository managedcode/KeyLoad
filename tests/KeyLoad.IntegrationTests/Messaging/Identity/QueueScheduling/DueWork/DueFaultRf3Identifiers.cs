using System.Globalization;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueFaultRf3Identifiers
{
    private const long InitialScheduleGeneration = 1;
    private const long InitialOccurrenceOrdinal = 0;
    private const long InitialSagaRevision = 1;

    internal static string Occurrence(Guid scheduleId)
        => string.Concat("recurring-", scheduleId.ToString("N"), "-",
            InitialScheduleGeneration.ToString("x16", CultureInfo.InvariantCulture), "-",
            InitialOccurrenceOrdinal.ToString("x16", CultureInfo.InvariantCulture));

    internal static string TimeoutMessage(Guid sagaId)
        => string.Concat("saga-timeout-", sagaId.ToString("N"), "-",
            InitialSagaRevision.ToString("x16", CultureInfo.InvariantCulture));
}
