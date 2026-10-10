using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferCoordinatorFixtureConfiguration
{
    private const string AttemptSetting = "KeyLoad__DatabaseLimits__MaxQueueTransferAcceptAttempts";
    private const string RepairSetting = "KeyLoad__DatabaseLimits__MaxQueueTransferRepairAttempts";
    private const string Setting = "KeyLoad__DueCoordination__TransferCoordinatorPrincipalId";

    internal static RemoteTransferCoordinatorFixtureSelection Validate(RemoteTransferCoordinatorFixtureSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        JsonData.Identifier(selection.PrincipalId);
        new DatabaseLimits
        {
            MaxQueueTransferAcceptAttempts = selection.AcceptAttemptCeiling,
            MaxQueueTransferRepairAttempts = selection.RepairCeiling
        }.Validate();
        return selection;
    }

    internal static void Configure(IDistributedApplicationTestingBuilder builder,
        RemoteTransferCoordinatorFixtureSelection? selection)
    {
        if (selection is null)
        { return; }
        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithEnvironment(Setting, selection.PrincipalId);
            if (selection.AcceptAttemptCeiling is { } ceiling)
            { node.WithEnvironment(AttemptSetting, ceiling.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            if (selection.RepairCeiling is { } repair)
            { node.WithEnvironment(RepairSetting, repair.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        }
    }
}
