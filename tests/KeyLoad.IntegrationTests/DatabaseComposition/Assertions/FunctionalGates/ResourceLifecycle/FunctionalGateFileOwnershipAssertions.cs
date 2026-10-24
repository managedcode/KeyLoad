using System.Globalization;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.StorageRecovery;
using KeyLoad.AppHost.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateFileOwnershipProtocol;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

/// <summary>Compares execution configured paths and exact process launches for the live-file refusal operation.</summary>
internal static class FunctionalGateFileOwnershipAssertions
{
    private const string DataEnvironment = "KeyLoad__DataDirectory";
    private const string ProjectProperty = "project.path";
    private const string ExecutableProperty = "executable.path";
    private const string ArgumentsProperty = "executable.args";
    private const string WorkDirectoryProperty = "executable.workDir";

    internal static async Task ModelAsync(FunctionalGateFixture fixture)
    {
        await Assert.That(fixture.NodeNames.Length).IsEqualTo(P.SiloCount);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in fixture.NodeNames)
        {
            var path = Directory(fixture, name);
            await Assert.That(path).IsEqualTo(Path.GetFullPath(Path.Combine(fixture.Root, name)));
            await Assert.That(paths.Add(path)).IsTrue();
        }
        // Contenders start explicitly and an unstarted resource has no process environment yet; their data directory is
        // compared with the owner's after each actual launch.
        foreach (var name in ExclusiveOwnerContentionResources.ResourceNames)
        {
            await Assert.That(Snapshot(fixture, name).Snapshot.State?.Text).IsEqualTo(KnownResourceStates.NotStarted);
        }
    }

    internal static string Directory(FunctionalGateFixture fixture, string name)
    {
        var path = Snapshot(fixture, name).Snapshot.EnvironmentVariables.Single(value => value.Name == DataEnvironment).Value;
        if (path is null || !Path.IsPathFullyQualified(path) || path.Length > FunctionalGatePhysicalFileProtocol.MaximumPathCharacters)
        {
            throw new InvalidOperationException(P.Incomplete);
        }
        return Path.GetFullPath(path);
    }

    private static ResourceEvent Snapshot(FunctionalGateFixture fixture, string name)
    {
        _ = fixture.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ProjectResource>()
            .Single(resource => resource.Name == name);
        if (!fixture.App.ResourceNotifications.TryGetCurrentState(name, out var actual)
            || actual.Resource is not ProjectResource || actual.Resource.Name != name)
        {
            throw new InvalidOperationException(P.Incomplete);
        }
        return actual;
    }

    internal static async Task<(string Source, string Project, string Executable, int ProcessId,
        string Launch, string Inspection)> RuntimeAsync(FunctionalGateFixture fixture, string name, bool refused,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var actual = Snapshot(fixture, name);
        var snapshot = actual.Snapshot;
        if (refused)
        {
            await Assert.That(snapshot.ExitCode).IsNotNull();
            await Assert.That(snapshot.ExitCode!.Value).IsNotEqualTo(P.SuccessExit);
        }
        else
        {
            await Assert.That(snapshot.State?.Text).IsEqualTo(KnownResourceStates.Running);
        }
        var project = (string)snapshot.Properties.Single(value => value.Name == ProjectProperty).Value!;
        var executable = (string)snapshot.Properties.Single(value => value.Name == ExecutableProperty).Value!;
        var args = (IEnumerable<string>)snapshot.Properties.Single(value => value.Name == ArgumentsProperty).Value!;
        var directory = (string)snapshot.Properties.Single(value => value.Name == WorkDirectoryProperty).Value!;
        var value = snapshot.Properties.Single(property => property.Name == ProcessFaultCommandProtocol.ProcessIdProperty).Value;
        if (!int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None,
                CultureInfo.InvariantCulture, out var processId) || processId <= 0
            || snapshot.StartTimeStamp is not { } started
            || !Path.IsPathFullyQualified(project) || string.IsNullOrWhiteSpace(executable)
            || !string.Equals(Path.GetFullPath(project),
                Path.GetFullPath(((ProjectResource)actual.Resource).GetProjectMetadata().ProjectPath), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(P.Incomplete);
        }
        var launch = JsonSerializer.Serialize(new { project, executable, arguments = args.ToArray(), directory });
        var inspection = JsonSerializer.Serialize(new { resource = name, processId, started, snapshot.ExitCode, launch });
        return (Directory(fixture, name), project, executable, processId, launch, inspection);
    }

    internal static bool HasOriginalRefusal(string text)
        => text.Contains(P.IoFailure, StringComparison.Ordinal) && text.Contains(P.LockPath, StringComparison.Ordinal)
            && text.Contains(P.OriginalOwner, StringComparison.Ordinal);
}
