namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueFaultRf3Protocol
{
    internal const string RecurringQueue = "due-recurring";
    internal const string SagaQueue = "due-sagas";
    internal const string TimeoutQueue = "due-timeouts";
    internal const string RecurringPayload = "{\"kind\":\"scheduled\",\"ordinal\":0}";
    internal const string RecurringHeaders = "{\"source\":\"due-rf3\"}";
    internal const string TimeoutPayload = "{\"kind\":\"timeout\"}";
    internal const string TimeoutHeaders = "{\"source\":\"due-rf3\"}";
    internal const string PayloadField = "/kind";
    internal const string HeaderField = "/source";
    internal const string Classification = "due-rf3-private";
    internal const string ReadGrant = "due.rf3.read";
    internal const string UseGrant = "due.rf3.use";
    internal const string WriteGrant = "due.rf3.write";
    internal const string PrincipalPrefix = "due-rf3-principal-";
    internal const string ApiKeyPrefix = "due-rf3-key-";
    internal const string SecretSeparator = ".";
    internal const int SecretBytes = 32;
    internal const string InspectScheduleTool = "keyload_schedule_inspect";
    internal const string InspectSagaTool = "keyload_saga_inspect";
    internal const string InspectMessageTool = "keyload_messages_inspect";
    internal const string QualificationPrefix = "messaging-due-rf3-";
    internal const string Database = "database";
    internal const string Domain = "due-rf3";
    internal const string SetupLeadFailure = "The due records were not fully seeded before the required RF3 lead time.";
    internal const int DueDelaySeconds = 90;
    internal const int MinimumSetupLeadSeconds = 30;
    internal const int PollMilliseconds = 250;
    internal const int PageMessages = 2;
    internal static readonly TimeSpan RecurrenceInterval = TimeSpan.FromHours(1);
    internal static readonly TimeSpan TimeoutTtl = TimeSpan.FromHours(2);
}
