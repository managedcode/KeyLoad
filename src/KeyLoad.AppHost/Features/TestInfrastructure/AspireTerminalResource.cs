using System.Globalization;
using System.Text;
using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class AspireTerminalResource
{
    private const string FailureFormat = "Aspire resource '{0}' reached terminal state '{1}' with original exit '{2}' before owned execution completed.";
    private const string ExitOnly = "exit-observed";
    private const string MissingExit = "unavailable";
    private const string CompletionPending = "completion-pending";
    private static readonly CompositeFormat FailureTemplate = CompositeFormat.Parse(FailureFormat);

    internal static bool IsTerminal(CustomResourceSnapshot snapshot)
        => snapshot.ExitCode is not null || snapshot.State?.Text is KnownResourceStates.FailedToStart
            or KnownResourceStates.RuntimeUnhealthy or KnownResourceStates.Finished or KnownResourceStates.Exited;

    internal static int RunnerExit(string resourceName, CustomResourceSnapshot snapshot)
    {
        if (snapshot.State?.Text is KnownResourceStates.FailedToStart or KnownResourceStates.RuntimeUnhealthy || snapshot.ExitCode is null)
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
            && snapshot.State?.Text is not (KnownResourceStates.FailedToStart or KnownResourceStates.RuntimeUnhealthy))
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
        KnownResourceStates.FailedToStart => KnownResourceStates.FailedToStart,
        KnownResourceStates.RuntimeUnhealthy => KnownResourceStates.RuntimeUnhealthy,
        KnownResourceStates.Finished => KnownResourceStates.Finished,
        KnownResourceStates.Exited => KnownResourceStates.Exited,
        _ => ExitOnly
    };
}
