using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owns only selected genuine SIGKILL phases and native Aspire restoration of their retained node data.</summary>
internal sealed class IsolatedKeyLoadFaultRegressionNative(DistributedApplication app, int nodeCount)
{
    private readonly IsolatedKeyLoadFaultRegressionNativeNode[] nodes = ReadNodes(app, nodeCount);
    internal List<IsolatedKeyLoadFaultRegressionNativeReceipt> Receipts { get; } = [];

    internal async Task<IsolatedKeyLoadFaultRegressionNativeReceipt> KillAsync(int index, CancellationToken token)
    {
        var node = nodes[index - 1];
        var before = await node.InspectAsync(token);
        IsolatedKeyLoadFaultRegressionProtocol.Require(before.State == IsolatedKeyLoadFaultRegressionProtocol.Running
            && before.StartedAt > DateTimeOffset.UnixEpoch);
        var receipt = new IsolatedKeyLoadFaultRegressionNativeReceipt(index, before, TimeProvider.System.GetUtcNow());
        Receipts.Add(receipt);
        var killed = await IsolatedKeyLoadFaultRegressionDocker.RunAsync(["kill", "--signal", "KILL", before.Id], token);
        IsolatedKeyLoadFaultRegressionProtocol.Require(killed == before.Id);
        receipt.KillCompletedAt = TimeProvider.System.GetUtcNow();
        receipt.Exited = await WaitExitedAsync(node, before, token);
        return receipt;
    }

    internal async Task RestartAsync(IsolatedKeyLoadFaultRegressionNativeReceipt receipt, CancellationToken token)
    {
        IsolatedKeyLoadFaultRegressionProtocol.Require(receipt.Exited is not null && receipt.Restored is null);
        using var deadline = IsolatedKeyLoadFaultRegressionProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultRestartTimeout, token);
        var node = nodes[receipt.Node - 1];
        var commands = app.Services.GetRequiredService<ResourceCommandService>();
        var result = await commands.ExecuteCommandAsync(node.Resource, KnownResourceCommands.StartCommand, deadline.Token);
        IsolatedKeyLoadFaultRegressionProtocol.Require(result.Success);
        await FinishRestartAsync(node, receipt, deadline.Token);
    }

    internal async Task<bool> RestoreAllAsync()
    {
        var success = true;
        foreach (var receipt in Receipts.Where(item => item.Restored is null))
        {
            try
            {
                await IsolatedKeyLoadFaultRegressionExecution.RunAsync(() => RestoreWithDeadlineAsync(receipt));
            }
            catch (ComparisonFailureException)
            {
                receipt.CleanupFailed = true;
                success = false;
            }
        }
        return success;
    }

    private async Task RestoreWithDeadlineAsync(IsolatedKeyLoadFaultRegressionNativeReceipt receipt)
    {
        using var cleanup = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultRestartTimeout);
        await RestoreOneAsync(receipt, cleanup.Token);
    }

    private async Task RestoreOneAsync(IsolatedKeyLoadFaultRegressionNativeReceipt receipt, CancellationToken token)
    {
        var node = nodes[receipt.Node - 1];
        var current = await node.InspectAsync(token);
        if (current.State != IsolatedKeyLoadFaultRegressionProtocol.Exited)
        {
            if (current.State == IsolatedKeyLoadFaultRegressionProtocol.Running
                && current.StartedAt == receipt.Before.StartedAt && current.Id == receipt.Before.Id)
            {
                return;
            }
            IsolatedKeyLoadFaultRegressionProtocol.Require(receipt.Exited is not null);
            await FinishRestartAsync(node, receipt, token);
            return;
        }
        ValidateExited(receipt.Before, current);
        receipt.Exited = current;
        await RestartAsync(receipt, token);
    }

    private async Task FinishRestartAsync(IsolatedKeyLoadFaultRegressionNativeNode node,
        IsolatedKeyLoadFaultRegressionNativeReceipt receipt, CancellationToken token)
    {
        await app.ResourceNotifications.WaitForResourceHealthyAsync(node.Resource, WaitBehavior.WaitOnResourceUnavailable, token);
        var restored = await WaitRunningAsync(node, receipt.Before, token);
        receipt.Restored = restored;
        receipt.RestoredAt = TimeProvider.System.GetUtcNow();
    }

    private static async Task<IsolatedKeyLoadFaultRegressionNativeIdentity> WaitRunningAsync(
        IsolatedKeyLoadFaultRegressionNativeNode node, IsolatedKeyLoadFaultRegressionNativeIdentity before, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var current = await node.InspectAsync(token);
            IsolatedKeyLoadFaultRegressionProtocol.Require(current.Image == before.Image
                && current.ImageId == before.ImageId && current.MountSha256 == before.MountSha256);
            if (current.State == IsolatedKeyLoadFaultRegressionProtocol.Running && current.StartedAt > before.StartedAt)
            {
                return current;
            }
            await Task.Delay(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultPollInterval, token);
        }
    }

    private static async Task<IsolatedKeyLoadFaultRegressionNativeIdentity> WaitExitedAsync(
        IsolatedKeyLoadFaultRegressionNativeNode node, IsolatedKeyLoadFaultRegressionNativeIdentity before, CancellationToken token)
    {
        using var deadline = IsolatedKeyLoadFaultRegressionProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultExitedTimeout, token);
        while (true)
        {
            var current = await node.InspectAsync(deadline.Token);
            IsolatedKeyLoadFaultRegressionProtocol.Require(current.Id == before.Id);
            if (current.State == IsolatedKeyLoadFaultRegressionProtocol.Exited)
            {
                ValidateExited(before, current);
                return current;
            }
            await Task.Delay(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultPollInterval, deadline.Token);
        }
    }

    private static void ValidateExited(IsolatedKeyLoadFaultRegressionNativeIdentity before, IsolatedKeyLoadFaultRegressionNativeIdentity current)
        => IsolatedKeyLoadFaultRegressionProtocol.Require(current.State == IsolatedKeyLoadFaultRegressionProtocol.Exited
            && current.Id == before.Id && current.Image == before.Image && current.ImageId == before.ImageId
            && current.MountSha256 == before.MountSha256);

    private static IsolatedKeyLoadFaultRegressionNativeNode[] ReadNodes(DistributedApplication app, int count)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var selected = model.Resources.OfType<ContainerResource>().Where(item => item.Name.StartsWith("node", StringComparison.Ordinal)).ToArray();
        IsolatedKeyLoadFaultRegressionProtocol.Require(selected.Length == count);
        return Enumerable.Range(1, count).Select(index => IsolatedKeyLoadFaultRegressionNativeNode.Read(
            selected.Single(item => item.Name == IsolatedKeyLoadFaultRegressionProtocol.Resource(index)), index)).ToArray();
    }
}
