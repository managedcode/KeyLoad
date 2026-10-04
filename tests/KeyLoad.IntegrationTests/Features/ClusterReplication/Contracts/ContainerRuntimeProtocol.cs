using System.Text;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Names the original native commands, polling bounds, diagnostic text and receipt paths.</summary>
internal static class ContainerRuntimeProtocol
{
    internal const string DockerExecutable = "docker";
    internal const string GitExecutable = "git";
    internal const string KillCommand = "kill";
    internal const string SignalArgument = "--signal";
    internal const string KillSignal = "KILL";
    internal const string KillOperation = "SIGKILL";
    internal const string InspectCommand = "inspect";
    internal const string FormatArgument = "--format";
    internal const string InspectFormat = "{{.Id}}|{{.Config.Image}}|{{.Image}}|{{.State.Status}}|{{.State.StartedAt}}";
    internal const string GitRevisionCommand = "rev-parse";
    internal const string GitHeadArgument = "HEAD";
    internal const string RunningState = "running";
    internal const string ExitedState = "exited";
    internal const string ArtifactsDirectory = "artifacts";
    internal const string QualificationDirectory = "qualification";
    internal static readonly CompositeFormat ReceiptFileName = CompositeFormat.Parse("rf3-container-restart-{0}-{1}.json");
    internal static readonly CompositeFormat BeforeKillFailure = CompositeFormat.Parse("Aspire container '{0}' is '{1}' before SIGKILL.");
    internal static readonly CompositeFormat ExitFailure = CompositeFormat.Parse("Aspire container '{0}' did not exit after Docker SIGKILL.");
    internal static readonly CompositeFormat MissingKillFailure = CompositeFormat.Parse("No verified SIGKILL receipt exists for Aspire container '{0}'.");
    internal static readonly CompositeFormat StartFailure = CompositeFormat.Parse("Aspire failed to start '{0}': {1}");
    internal const string UnknownCommandFailure = "unknown command failure";
    internal static readonly CompositeFormat UnchangedStartFailure = CompositeFormat.Parse("Aspire container '{0}' became healthy without a new Docker runtime start timestamp.");
    internal static readonly CompositeFormat MissingContainerFailure = CompositeFormat.Parse("Aspire model has no explicit container name for '{0}'.");
    internal static readonly CompositeFormat RunningFailure = CompositeFormat.Parse("Aspire container '{0}' did not return to running state after resource-start.");
    internal static readonly CompositeFormat InspectFailure = CompositeFormat.Parse("Docker inspect returned an invalid container receipt for '{0}'.");
    internal const string DockerStartFailure = "Could not start the Docker CLI.";
    internal const string GitStartFailure = "Could not start git for the container receipt.";
    internal static readonly CompositeFormat GitRevisionFailure = CompositeFormat.Parse("git rev-parse failed: {0}");
    internal static readonly CompositeFormat DockerFailure = CompositeFormat.Parse("Docker {0} failed for '{1}' (exit {2}): {3}");
    internal const char InspectSeparator = '|';
    internal const int SuccessfulExitCode = 0;
    internal const int InspectFieldCount = 5;
    internal const int IdField = 0;
    internal const int ConfigImageField = 1;
    internal const int ImageIdField = 2;
    internal const int StateField = 3;
    internal const int StartedAtField = 4;
    internal const int FullContainerIdCharacters = 64;
    internal const int DiagnosticCharacters = 1_024;
    internal static readonly TimeSpan ContainerExitTimeout = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan ContainerStartTimeout = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan ExitPollInterval = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan StartPollInterval = TimeSpan.FromMilliseconds(200);
}
