using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateFileOwnershipProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Joins every original competing container before its live owner's files can be released.</summary>
internal static class FunctionalGateFileOwnershipProbe
{
    internal static async Task RunAsync(FunctionalGateFixture fixture, CancellationToken token)
    {
        var settlement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.RegisterFileContenderSettlement(settlement.Task);
        var tasks = new List<Task<string>>();
        var ids = new List<string>();
        var settled = new bool[P.SiloCount];
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await FunctionalGateFileOwnershipAssertions.ModelAsync(fixture);
                var owner = await FunctionalGateFileOwnershipAssertions.RuntimeAsync(fixture,
                    ExclusiveOwnerContentionResources.OwnerName, refused: false, token);
                var saved = fixture.PhysicalFiles!.Owners.Single(value => value.Resource == ExclusiveOwnerContentionResources.OwnerName);
                await Assert.That(owner.Id).IsEqualTo(saved.Runtime.Id);
                await Assert.That(owner.Image).IsEqualTo(saved.Runtime.ImageId);
                await Assert.That(owner.Configured).IsEqualTo(saved.Runtime.ConfigImage);
                await Assert.That(owner.Source).IsEqualTo(saved.Directory);
                var output = new ClusterFailureReceipts().RequireRunOutput(TestContext.ResultsDirectory);
                var names = ExclusiveOwnerContentionResources.ResourceNames;
                await Assert.That(names.Length).IsEqualTo(P.SiloCount);
                for (var index = P.FirstContender; index < names.Length; index++)
                {
                    var slot = index;
                    tasks.Add(RunOneAsync(fixture, names[index], owner, output, value => settled[slot] = value, token));
                }
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                foreach (var task in tasks)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        ids.Add(await task.ConfigureAwait(false));
                    }, failures);
                }
            }
            finally
            {
                _ = settlement.TrySetResult(tasks.All(task => task.IsCompleted) && settled.Take(tasks.Count).All(value => value));
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(ids.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(P.SiloCount);
    }

    private static async Task<string> RunOneAsync(FunctionalGateFixture fixture, string name,
        (string Source, string Image, string Configured, string Id, string Inspection) owner,
        string results, Action<bool> settle, CancellationToken token)
    {
        var stopped = false;
        var startAccepted = false;
        var exited = false;
        try
        {
            using var bound = new CancellationTokenSource(P.ProbeTimeout, TimeProvider.System);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, bound.Token);
            var failures = new List<Exception>();
            var commands = fixture.App.Services.GetRequiredService<ResourceCommandService>();
            var logs = FunctionalGateFileOwnershipLogs.ReadAsync(fixture, name, lifetime.Token);
            var terminal = WaitForExitAsync(fixture, name, lifetime.Token);
            var id = string.Empty;
            try
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var started = await commands.ExecuteCommandAsync(name, KnownResourceCommands.StartCommand, lifetime.Token);
                    startAccepted = started.Success;
                    await Assert.That(startAccepted).IsTrue();
                    _ = await terminal.ConfigureAwait(false);
                    exited = true;
                    var text = await logs.ConfigureAwait(false);
                    var actual = await FunctionalGateFileOwnershipAssertions.RuntimeAsync(fixture, name, refused: true, lifetime.Token);
                    await Assert.That(actual.Source).IsEqualTo(owner.Source);
                    await Assert.That(actual.Image).IsEqualTo(owner.Image);
                    await Assert.That(actual.Configured).IsEqualTo(owner.Configured);
                    await Assert.That(actual.Id).IsNotEqualTo(owner.Id);
                    id = actual.Id;
                    var output = Path.Combine(results, P.EvidenceDirectory, name);
                    var logsDirectory = Path.Combine(output, P.LogEvidence);
                    var runtimeDirectory = Path.Combine(output, P.RuntimeEvidence);
                    Directory.CreateDirectory(logsDirectory);
                    Directory.CreateDirectory(runtimeDirectory);
                    _ = ClusterFailureReceipts.SaveImmutable(logsDirectory, [text]);
                    _ = ClusterFailureReceipts.SaveImmutable(runtimeDirectory, [owner.Inspection, actual.Inspection]);
                }, failures).ConfigureAwait(false);
            }
            finally
            {
                var ownerStopped = await StopAsync(fixture, name, failures).ConfigureAwait(false);
                ServerFailureObserver.Observe(lifetime.Cancel, failures);
                await ServerFailureObserver.ObserveAsync(() => JoinObservationAsync(logs, lifetime.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => JoinObservationAsync(terminal, lifetime.Token), failures);
                stopped = ownerStopped && logs.IsCompleted && terminal.IsCompleted;
            }
            ServerFailureObserver.ThrowIfAny(failures);
            return id;
        }
        finally
        {
            settle(stopped && startAccepted && exited);
        }
    }

    private static async Task<bool> StopAsync(FunctionalGateFixture fixture, string name, List<Exception> failures)
    {
        var before = failures.Count;
        using var cleanup = new CancellationTokenSource(fixture.App.Services
            .GetRequiredService<IOptions<KeyLoad.AppHost.Features.TestInfrastructure.TestExecutionOptions>>()
            .Value.ApplicationCleanupTimeout, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var stopped = await fixture.App.Services.GetRequiredService<ResourceCommandService>()
                .ExecuteCommandAsync(name, KnownResourceCommands.StopCommand, cleanup.Token);
            await Assert.That(stopped.Success).IsTrue();
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await ContainerRuntimeDocker.WaitForExitedAsync(
                FunctionalGateFileOwnershipAssertions.ContainerName(fixture, name), name, cleanup.Token);
        }, failures).ConfigureAwait(false);
        return failures.Count == before;
    }

    private static async Task<ResourceEvent> WaitForExitAsync(FunctionalGateFixture fixture, string name, CancellationToken token)
    {
        await foreach (var update in fixture.App.ResourceNotifications.WatchAsync(token).ConfigureAwait(false))
        {
            if (update.Resource.Name != name)
            {
                continue;
            }
            await Assert.That(update.Snapshot.HealthStatus == HealthStatus.Healthy).IsFalse();
            if (update.Snapshot.ExitCode is { } exit)
            {
                await Assert.That(exit).IsNotEqualTo(P.SuccessExit);
                return update;
            }
        }
        throw new InvalidOperationException(P.Incomplete);
    }

    private static async Task JoinObservationAsync(Task observation, CancellationToken token)
    {
        try
        {
            await observation.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }
}
