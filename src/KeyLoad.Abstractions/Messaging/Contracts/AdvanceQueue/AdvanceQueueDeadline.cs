namespace KeyLoad;

/// <summary>Conditionally advances a canonical queue deadline using execution command time.</summary>
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
