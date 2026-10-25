namespace KeyLoad.FunctionalTests.Messaging;

internal static class RecurringSagaSchemaProtocol
{
    internal const string Queue = "schema-saga-jobs";
    internal const string Reader = "schema-saga-reader";
    internal const string Denied = "schema-saga-denied";
    internal const string Scheduler = "schema-saga-scheduler";
    internal const string WriteGrant = "pii.write";
    internal const string SecretPath = "/secret";
    internal const string BodySchema = "schema.saga.body";
    internal const string HeaderSchema = "schema.saga.header";
    internal const string Body = """{"public":"body","secret":"SCHEMA-SAGA-BODY-CANARY"}""";
    internal const string Headers = """{"public":"header","secret":"SCHEMA-SAGA-HEADER-CANARY"}""";
    internal const string SafeBody = """{"public":"body"}""";
    internal const string SafeHeaders = """{"public":"header"}""";
    internal const string ScheduleKind = "configureRecurringSchedule";
    internal const string SagaKind = "compareExchangeSaga";
    internal const string Format = "N";
    internal const string Outbox = "outbox";
    internal const string OutboxHeadSpace = "outbox-head";
    internal const string Utc = "UTC";
    internal const string DeniedDetail = "The principal cannot perform this operation in this scope.";
    internal const int Zero = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int ColdOwners = 2;
    internal const long InitialEpoch = 1;
    internal const long FinalEpoch = 5;
    internal static readonly Guid ScheduleId = Guid.Parse("735728b0-5738-4ac8-a713-8f3fd03b15b8");
    internal static readonly Guid SagaId = Guid.Parse("4bbabf86-b12c-4d4d-8dce-e3dfd80714cb");
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);
}
