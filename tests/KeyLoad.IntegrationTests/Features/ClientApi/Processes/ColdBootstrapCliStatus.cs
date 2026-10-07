using System.Diagnostics;
using System.Text.Json;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.TestInfrastructure.Processes;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class ColdBootstrapCliStatus
{
    private const string Dotnet = "dotnet";
    private const string CliAssembly = "src/KeyLoad.Cli/bin/Release/net10.0/KeyLoad.Cli.dll";
    private const string StatusCommand = "status";
    private const string CredentialEnvironment = "KEYLOAD_API_KEY";
    private const string Missing = "The actual bootstrap CLI process or typed status was absent.";
    private const int Success = 0;
    private const int Voters = 3;

    internal static async Task VerifyAsync(ClusterFixture fixture, NodeStatus expected, CancellationToken token)
    {
        var policy = fixture.App.Services.GetRequiredService<IOptions<TestExecutionOptions>>();
        var start = new ProcessStartInfo(Dotnet)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, CliAssembly));
        start.ArgumentList.Add(StatusCommand);
        start.ArgumentList.Add(fixture.App.GetEndpoint(McpCallerProtocol.Node1, McpCallerProtocol.HttpEndpoint).AbsoluteUri);
        start.ArgumentList.Add(Path.Combine(fixture.Root, ClusterFixtureProtocol.ProfileFileName));
        start.Environment.Remove(CredentialEnvironment);
        using var process = new Process { StartInfo = start };
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (!process.Start())
            { throw new InvalidOperationException(Missing); }
            await VerifyStartedAsync(process, fixture, expected, policy, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(process.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyStartedAsync(Process process, ClusterFixture fixture, NodeStatus expected,
        IOptions<TestExecutionOptions> policy, CancellationToken token)
    {
        var exit = process.WaitForExitAsync(CancellationToken.None);
        var output = LocalRf3OwnedProcessLifetime.ReadBoundedAsync(process.StandardOutput, policy.Value.CleanupOutputCharacters);
        var error = LocalRf3OwnedProcessLifetime.ReadBoundedAsync(process.StandardError, policy.Value.CleanupOutputCharacters);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => LocalRf3OwnedProcessLifetime.ObserveAsync(exit, output, error,
            TimeProvider.System, token), failures).ConfigureAwait(false);
        if (failures.Count != Success || !exit.IsCompleted || !output.IsCompleted || !error.IsCompleted)
        { await LocalRf3OwnedProcessLifetime.TerminateAndJoinAsync(process, exit, output, error, failures, policy, TimeProvider.System).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(process.ExitCode).IsEqualTo(Success);
        await Assert.That(await error.ConfigureAwait(false)).IsEqualTo(string.Empty);
        var text = await output.ConfigureAwait(false);
        await Assert.That(text.Contains(fixture.AdminKey, StringComparison.Ordinal)).IsFalse();
        var actual = JsonSerializer.Deserialize<NodeStatus>(text, JsonDefaults.Options) ?? throw new InvalidOperationException(Missing);
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.RoutingReady).IsTrue();
        await Assert.That(actual.Voters).IsEqualTo(Voters);
        await Assert.That(actual.ProcessId).IsGreaterThan(Success);
    }
}
