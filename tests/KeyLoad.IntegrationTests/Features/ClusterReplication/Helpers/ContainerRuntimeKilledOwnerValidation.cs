using Aspire.Hosting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ContainerRuntimeKilledOwnerValidation
{
    internal static IReadOnlyDictionary<string, ContainerRuntimeKillReceipt> Require(
        DistributedApplication owner, DistributedApplication actualOriginal,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, ContainerRuntimeKillReceipt> originalReceipts)
    {
        if (!ReferenceEquals(owner, actualOriginal) || names.Count != originalReceipts.Count
            || names.Keys.Any(name => !originalReceipts.ContainsKey(name)))
        { throw new InvalidOperationException(ContainerRestartOwnership.PendingFailure); }
        foreach (var pair in names)
        {
            var original = originalReceipts[pair.Key];
            if (original.ResourceName != pair.Key || original.ContainerName != pair.Value
                || original.KillExitCode != ContainerRuntimeProtocol.SuccessfulExitCode
                || original.Before.State != ContainerRuntimeProtocol.RunningState
                || original.Stopped.State != ContainerRuntimeProtocol.ExitedState
                || original.Before.Id.Length != ContainerRuntimeProtocol.FullContainerIdCharacters
                || original.Before.Id != original.Stopped.Id
                || original.Before.ConfigImage != original.Stopped.ConfigImage
                || original.Before.ImageId != original.Stopped.ImageId
                || original.Before.StartedAt != original.Stopped.StartedAt)
            { throw new InvalidOperationException(ContainerRestartOwnership.PendingFailure); }
        }
        return originalReceipts;
    }
}
