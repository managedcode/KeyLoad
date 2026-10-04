using System.Globalization;
using System.Text;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class AspireTerminalResource
{
    private const string FailureFormat = "Aspire resource '{0}' reached terminal state '{1}' with original exit '{2}' before owned execution completed.";
    private const string ExitOnly = "exit-observed";
    private const string MissingExit = "unavailable";
    private const string CompletionPending = "completion-pending";
    private static readonly CompositeFormat FailureTemplate = CompositeFormat.Parse(FailureFormat);

    internal static bool IsTerminal(CustomResourceSnapshot snapshot)
        => snapshot.ExitCode is not null || HasFailedState(snapshot)
            || string.Equals(snapshot.State?.Text, KnownResourceStates.Finished, StringComparison.Ordinal)
            || string.Equals(snapshot.State?.Text, KnownResourceStates.Exited, StringComparison.Ordinal);

    internal static bool HasFailedState(CustomResourceSnapshot snapshot)
        => string.Equals(snapshot.State?.Text, KnownResourceStates.FailedToStart, StringComparison.Ordinal)
            || string.Equals(snapshot.State?.Text, KnownResourceStates.RuntimeUnhealthy, StringComparison.Ordinal);

    internal static int RunnerExit(string resourceName, CustomResourceSnapshot snapshot)
    {
        if (HasFailedState(snapshot) || snapshot.ExitCode is null)
        {
            throw Failure(resourceName, snapshot);
        }
        return snapshot.ExitCode.Value;
    }

    internal static void ValidateDependency(string resourceName, CustomResourceSnapshot snapshot, int? expectedExit)
    {
        if (!IsTerminal(snapshot))
        {
            return;
        }
        if (expectedExit is not null && snapshot.ExitCode == expectedExit
            && !HasFailedState(snapshot))
        {
            return;
        }
        throw Failure(resourceName, snapshot);
    }

    internal static void RequireCompletions(HashSet<string> pending)
    {
        if (pending.Count != 0)
        {
            throw new DistributedApplicationException(string.Format(CultureInfo.InvariantCulture, FailureTemplate,
                pending.First(), CompletionPending, MissingExit));
        }
    }

    private static DistributedApplicationException Failure(string resourceName, CustomResourceSnapshot snapshot)
        => new(string.Format(CultureInfo.InvariantCulture, FailureTemplate, resourceName, ClosedState(snapshot),
            snapshot.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? MissingExit));

    private static string ClosedState(CustomResourceSnapshot snapshot) => snapshot.State?.Text switch
    {
        var state when string.Equals(state, KnownResourceStates.FailedToStart, StringComparison.Ordinal) => KnownResourceStates.FailedToStart,
        var state when string.Equals(state, KnownResourceStates.RuntimeUnhealthy, StringComparison.Ordinal) => KnownResourceStates.RuntimeUnhealthy,
        var state when string.Equals(state, KnownResourceStates.Finished, StringComparison.Ordinal) => KnownResourceStates.Finished,
        var state when string.Equals(state, KnownResourceStates.Exited, StringComparison.Ordinal) => KnownResourceStates.Exited,
        _ => ExitOnly
    };
}
