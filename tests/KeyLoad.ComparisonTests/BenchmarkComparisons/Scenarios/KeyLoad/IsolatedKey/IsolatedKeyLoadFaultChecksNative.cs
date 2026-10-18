using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owns only selected genuine SIGKILL phases and native Aspire restoration of their retained node data.</summary>
internal sealed class IsolatedKeyLoadFaultChecksNative(DistributedApplication app, int nodeCount)
{
    private readonly IsolatedKeyLoadFaultChecksNativeNode[] nodes = ReadNodes(app, nodeCount);
    internal List<IsolatedKeyLoadFaultChecksNativeReceipt> Receipts { get; } = [];

    internal async Task<IsolatedKeyLoadFaultChecksNativeReceipt> KillAsync(int index, CancellationToken token)
    {
        var node = nodes[index - 1];
        var before = await node.InspectAsync(token);
        IsolatedKeyLoadFaultChecksProtocol.Require(before.State == IsolatedKeyLoadFaultChecksProtocol.Running
            && before.StartedAt > DateTimeOffset.UnixEpoch);
        var receipt = new IsolatedKeyLoadFaultChecksNativeReceipt(index, before, TimeProvider.System.GetUtcNow());
        Receipts.Add(receipt);
        var killed = await IsolatedKeyLoadFaultChecksDocker.RunAsync(["kill", "--signal", "KILL", before.Id], token);
        IsolatedKeyLoadFaultChecksProtocol.Require(killed == before.Id);
        receipt.KillCompletedAt = TimeProvider.System.GetUtcNow();
        receipt.Exited = await WaitExitedAsync(node, before, token);
        return receipt;
    }

    internal async Task RestartAsync(IsolatedKeyLoadFaultChecksNativeReceipt receipt, CancellationToken token)
    {
        IsolatedKeyLoadFaultChecksProtocol.Require(receipt.Exited is not null && receipt.Restored is null);
        using var deadline = IsolatedKeyLoadFaultChecksProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultRestartTimeout, token);
        var node = nodes[receipt.Node - 1];
        var commands = app.Services.GetRequiredService<ResourceCommandService>();
        var result = await commands.ExecuteCommandAsync(node.Resource, KnownResourceCommands.StartCommand, deadline.Token);
        IsolatedKeyLoadFaultChecksProtocol.Require(result.Success);
        await FinishRestartAsync(node, receipt, deadline.Token);
    }

    internal async Task<bool> RestoreAllAsync()
    {
        var success = true;
        foreach (var receipt in Receipts.Where(item => item.Restored is null))
        {
            try
            {
                await IsolatedKeyLoadFaultChecksExecution.RunAsync(() => RestoreWithDeadlineAsync(receipt));
            }
            catch (ComparisonFailureException)
            {
                receipt.CleanupFailed = true;
                success = false;
            }
        }
        return success;
    }

    private async Task RestoreWithDeadlineAsync(IsolatedKeyLoadFaultChecksNativeReceipt receipt)
    {
        using var cleanup = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultRestartTimeout, TimeProvider.System);
        await RestoreOneAsync(receipt, cleanup.Token);
    }

    private async Task RestoreOneAsync(IsolatedKeyLoadFaultChecksNativeReceipt receipt, CancellationToken token)
    {
        var node = nodes[receipt.Node - 1];
        var current = await node.InspectAsync(token);
        if (current.State != IsolatedKeyLoadFaultChecksProtocol.Exited)
        {
            if (current.State == IsolatedKeyLoadFaultChecksProtocol.Running
                && current.StartedAt == receipt.Before.StartedAt && current.Id == receipt.Before.Id)
            {
                return;
            }
            IsolatedKeyLoadFaultChecksProtocol.Require(receipt.Exited is not null);
            await FinishRestartAsync(node, receipt, token);
            return;
        }
        ValidateExited(receipt.Before, current);
        receipt.Exited = current;
        await RestartAsync(receipt, token);
    }

    private async Task FinishRestartAsync(IsolatedKeyLoadFaultChecksNativeNode node,
        IsolatedKeyLoadFaultChecksNativeReceipt receipt, CancellationToken token)
    {
        await app.ResourceNotifications.WaitForResourceHealthyAsync(node.Resource, WaitBehavior.WaitOnResourceUnavailable, token);
        var restored = await WaitRunningAsync(node, receipt.Before, token);
        receipt.Restored = restored;
        receipt.RestoredAt = TimeProvider.System.GetUtcNow();
    }

    private static async Task<IsolatedKeyLoadFaultChecksNativeIdentity> WaitRunningAsync(
        IsolatedKeyLoadFaultChecksNativeNode node, IsolatedKeyLoadFaultChecksNativeIdentity before, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var current = await node.InspectAsync(token);
            IsolatedKeyLoadFaultChecksProtocol.Require(current.Image == before.Image
                && current.ImageId == before.ImageId && current.MountSha256 == before.MountSha256);
            if (current.State == IsolatedKeyLoadFaultChecksProtocol.Running && current.StartedAt > before.StartedAt)
            {
                return current;
            }
            await Task.Delay(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultPollInterval, TimeProvider.System, token);
        }
    }

    private static async Task<IsolatedKeyLoadFaultChecksNativeIdentity> WaitExitedAsync(
        IsolatedKeyLoadFaultChecksNativeNode node, IsolatedKeyLoadFaultChecksNativeIdentity before, CancellationToken token)
    {
        using var deadline = IsolatedKeyLoadFaultChecksProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultExitedTimeout, token);
        while (true)
        {
            var current = await node.InspectAsync(deadline.Token);
            IsolatedKeyLoadFaultChecksProtocol.Require(current.Id == before.Id);
            if (current.State == IsolatedKeyLoadFaultChecksProtocol.Exited)
            {
                ValidateExited(before, current);
                return current;
            }
            await Task.Delay(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultPollInterval, TimeProvider.System, deadline.Token);
        }
    }

    private static void ValidateExited(IsolatedKeyLoadFaultChecksNativeIdentity before, IsolatedKeyLoadFaultChecksNativeIdentity current)
        => IsolatedKeyLoadFaultChecksProtocol.Require(current.State == IsolatedKeyLoadFaultChecksProtocol.Exited
            && current.Id == before.Id && current.Image == before.Image && current.ImageId == before.ImageId
            && current.MountSha256 == before.MountSha256);

    private static IsolatedKeyLoadFaultChecksNativeNode[] ReadNodes(DistributedApplication app, int count)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var selected = model.Resources.OfType<ContainerResource>().Where(item => item.Name.StartsWith("node", StringComparison.Ordinal)).ToArray();
        IsolatedKeyLoadFaultChecksProtocol.Require(selected.Length == count);
        return Enumerable.Range(1, count).Select(index => IsolatedKeyLoadFaultChecksNativeNode.Read(
            selected.Single(item => item.Name == IsolatedKeyLoadFaultChecksProtocol.Resource(index)), index)).ToArray();
    }
}
