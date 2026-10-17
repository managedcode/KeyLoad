using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementCleanupReadBudgetRf3Enrollment
{
    private const string ReadBudget = "KeyLoad__DatabaseLimits__MaxQueryReadBytes";

    internal static void Configure(IDistributedApplicationTestingBuilder builder, long? maximumBytes)
    {
        if (maximumBytes is not { } selected)
        {
            return;
        }
        new DatabaseLimits { MaxQueryReadBytes = selected }.Validate();
        var expected = TwoRf3MembershipProtocol.Nodes.Skip(TwoRf3MembershipProtocol.MembersPerGroup).ToArray();
        var targets = builder.Resources.OfType<ContainerResource>()
            .Where(resource => expected.Contains(resource.Name, StringComparer.Ordinal)).ToArray();
        if (targets.Length != expected.Length || !targets.Select(resource => resource.Name)
            .Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch);
        }
        foreach (var target in targets)
        {
            builder.CreateResourceBuilder(target).WithEnvironment(ReadBudget, selected.ToString(CultureInfo.InvariantCulture));
        }
    }
}
