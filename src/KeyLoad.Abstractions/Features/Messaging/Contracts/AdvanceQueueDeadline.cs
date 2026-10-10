namespace KeyLoad;

internal static class QueueDeadlineContract
{
    internal const string Alias = "keyload.mutation.advance-queue-deadline.v1";
    internal const string KindAlias = "keyload.messaging.queue-deadline-kind.v1";
    internal const string Discriminator = "advanceQueueDeadline";
    internal const string KindJsonName = "deadlineKind";
    internal const int QueueField = 0;
    internal const int MessageField = 1;
    internal const int StateField = 2;
    internal const int VersionField = 3;
    internal const int LeaseField = 4;
    internal const int DeadlineField = 5;
    internal const int KindField = 6;
}

/// <summary>The canonical deadline whose exact retained state is advanced.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(QueueDeadlineContract.KindAlias)]
public enum QueueDeadlineKind
{
    /// <summary>Make a scheduled message eligible at its retained NotBefore.</summary>
    PromoteScheduled = 0,
    /// <summary>Expire a retained lease and apply the existing retry/dead-letter policy.</summary>
    ExpireLease = 1,
    /// <summary>Expire a pending message at its retained absolute lifetime.</summary>
    ExpireMessage = 2
}

/// <summary>Conditionally advances a canonical queue deadline using native command time.</summary>
/// <param name="Queue">The owning queue.</param>
/// <param name="MessageId">The retained message identity.</param>
/// <param name="ExpectedState">The exact current pending state.</param>
/// <param name="ExpectedStateVersion">The exact current state version.</param>
/// <param name="ExpectedLeaseVersion">The exact current lease version.</param>
/// <param name="ExpectedDeadline">The original UTC deadline, never a trusted current time.</param>
/// <param name="Kind">The canonical transition to perform.</param>
[Orleans.GenerateSerializer, Orleans.Alias(QueueDeadlineContract.Alias)]
public sealed record AdvanceQueueDeadline(
    [property: Orleans.Id(QueueDeadlineContract.QueueField)] string Queue,
    [property: Orleans.Id(QueueDeadlineContract.MessageField)] string MessageId,
    [property: Orleans.Id(QueueDeadlineContract.StateField)] MessageState ExpectedState,
    [property: Orleans.Id(QueueDeadlineContract.VersionField)] long ExpectedStateVersion,
    [property: Orleans.Id(QueueDeadlineContract.LeaseField)] long ExpectedLeaseVersion,
    [property: Orleans.Id(QueueDeadlineContract.DeadlineField)] DateTimeOffset ExpectedDeadline,
    [property: Orleans.Id(QueueDeadlineContract.KindField), global::System.Text.Json.Serialization.JsonPropertyName(QueueDeadlineContract.KindJsonName)] QueueDeadlineKind Kind) : Mutation(Queue);
