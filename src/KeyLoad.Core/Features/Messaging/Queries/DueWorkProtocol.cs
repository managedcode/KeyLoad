using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

internal static class DueWorkProtocol
{
    internal const int MaximumRecordsPerPage = 32;
    internal const int MaximumKeyBytes = 4_096;
    internal const long NativeRangeByteCeiling = 67_108_864;
    internal const string ScheduleSpace = RecurringSagaProtocol.ScheduleSpace;
    internal const string SagaSpace = RecurringSagaProtocol.SagaSpace;
    internal const string InvalidWakeInstant = "The due-work wake instant must be UTC.";
    internal const string InvalidCursor = "The due-work cursor is inconsistent.";
    internal const string KeyExceedsBound = "A due-work key exceeds the bounded cursor size.";
    internal const string RangeBytesExceeded = "The due-work native range exceeds its fixed byte ceiling.";
    internal const string InvalidRecord = "A persisted due-work record is inconsistent.";
    internal const string DeadlineExceeded = "The due-work discovery deadline is exceeded.";
}
