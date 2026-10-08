namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Retains unsettled original Process and task ownership without disposing them or reopening either root.</summary>
internal static class ControlledPartitionMovementRetainedChild
{
    private const string UnsettledKey = "KeyLoad.ControlledMovement.UnsettledChild";
    private const string RootKey = "KeyLoad.ControlledMovement.RetainedRoot";
    private static readonly Lock Gate = new();
    private static readonly List<ControlledPartitionMovementTerminalProcessChild> Retained = [];

    internal static void Retain(ControlledPartitionMovementTerminalProcessChild original, string root,
        List<Exception> failures)
    {
        lock (Gate)
        { Retained.Add(original); }
        var failure = new IOException("The original movement child exit or readers did not join within cleanup.");
        failure.Data[UnsettledKey] = true;
        failure.Data[RootKey] = root;
        failures.Add(failure);
    }

    internal static bool ContainsUnsettled(Exception failure)
        => failure.Data[UnsettledKey] is true || failure is AggregateException aggregate
            && aggregate.InnerExceptions.Any(ContainsUnsettled)
            || failure.InnerException is { } inner && ContainsUnsettled(inner);
}
