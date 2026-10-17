using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>The closed selected resource resolves to its genuine container name and node-owned bind authority.</summary>
internal sealed record IsolatedKeyLoadFaultRegressionNativeNode(int Index, string Resource, string ContainerName,
    string Image, string DataSource)
{
    internal static IsolatedKeyLoadFaultRegressionNativeNode Read(ContainerResource resource, int index)
    {
        var name = resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name;
        var mount = resource.Annotations.OfType<ContainerMountAnnotation>()
            .Single(item => item.Target == IsolatedKeyLoadFaultRegressionProtocol.DataMount);
        IsolatedKeyLoadFaultRegressionProtocol.Require(resource.Name == IsolatedKeyLoadFaultRegressionProtocol.Resource(index)
            && name.StartsWith("keyload-", StringComparison.Ordinal) && name.Length <= 100
            && name.All(static item => char.IsAsciiLetterOrDigit(item) || item == '-')
            && mount.Type == ContainerMountType.BindMount && !mount.IsReadOnly && mount.Source is not null);
        IsolatedKeyLoadFaultRegressionProtocol.Require(resource.TryGetContainerImageName(out var image)
            && image!.Contains(ComparisonImageProtocol.ImageDigestPrefix, StringComparison.Ordinal));
        return new(index, resource.Name, name, image!, Path.GetFullPath(mount.Source!));
    }

    internal async Task<IsolatedKeyLoadFaultRegressionNativeIdentity> InspectAsync(CancellationToken token)
        => IsolatedKeyLoadFaultRegressionNativeIdentity.Parse(await IsolatedKeyLoadFaultRegressionDocker.RunAsync(
            ["inspect", "--format", IsolatedKeyLoadFaultRegressionNativeIdentity.InspectFormat, ContainerName], token), Image, DataSource);
}
