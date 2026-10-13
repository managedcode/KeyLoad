namespace KeyLoad.Server;

internal static class PostgresErrorDeliveryErrors
{
    internal static bool IsFirst(IReadOnlyList<Exception> failures, Exception primary)
        => failures.Count != PostgresErrorDeliveryProtocol.None
            && ReferenceEquals(First(failures[PostgresErrorDeliveryProtocol.PrimaryIndex]), primary);

    private static Exception First(Exception failure)
        => failure is AggregateException aggregate && aggregate.InnerExceptions.Count != PostgresErrorDeliveryProtocol.None
            ? First(aggregate.InnerExceptions[PostgresErrorDeliveryProtocol.PrimaryIndex]) : failure;

    internal static bool HasCancellation(Exception failure)
        => failure is OperationCanceledException || failure is AggregateException aggregate
            && aggregate.InnerExceptions.Any(HasCancellation);

    internal static int Count(IReadOnlyList<Exception> failures)
        => failures.Sum(CountOne);

    private static int CountOne(Exception failure)
        => failure is AggregateException aggregate ? aggregate.InnerExceptions.Sum(CountOne)
            : PostgresErrorDeliveryProtocol.One;
}
