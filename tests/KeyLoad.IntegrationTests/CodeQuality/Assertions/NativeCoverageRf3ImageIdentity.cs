using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3ImageIdentity
{
    private const string DockerImageCommand = "image";
    private const string DockerInspectCommand = "inspect";
    private const string DockerFormatOption = "--format";
    private const string DockerIdFormat = "{{json .Id}}";
    private const string RunningState = "running";
    private const string ExitedState = "exited";

    internal static async Task VerifyBeforeStartAsync(DistributedApplication app,
        NativeCoverageRf3FixtureContext context, CancellationToken cancellationToken)
    {
        _ = await ClusterFixtureImageIdentity.ReadVerifiedReferenceAsync(cancellationToken).ConfigureAwait(false);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var nodes = model.Resources.OfType<ContainerResource>()
            .Where(node => ClusterFixtureProtocol.IsNodeName(node.Name)).ToArray();
        if (nodes.Length != ClusterFixtureProtocol.NodeCount)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        foreach (var node in nodes)
        {
            var image = node.Annotations.OfType<ContainerImageAnnotation>().Single();
            if (image.Image != NativeCoverageRf3Protocol.CoverageImageRepository
                || image.Tag != context.RunId.Replace("-", string.Empty, StringComparison.Ordinal)
                || image.SHA256 is not null)
            {
                throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
            }
        }
        var inspect = await ContainerRuntimeDocker.RunAsync(
            [DockerImageCommand, DockerInspectCommand, context.ImageReference, DockerFormatOption, DockerIdFormat],
            cancellationToken).ConfigureAwait(false);
        ContainerRuntimeDocker.EnsureSuccessful(inspect, DockerInspectCommand, context.ImageReference);
        using var document = System.Text.Json.JsonDocument.Parse(inspect.StandardOutput);
        if (!string.Equals(document.RootElement.GetString(), context.ImageId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
    }

    internal static async Task<IReadOnlyDictionary<string, ContainerRuntimeInspection>>
        VerifyStartedAsync(IReadOnlyDictionary<string, string> names, NativeCoverageRf3FixtureContext context,
            CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ContainerRuntimeInspection>(StringComparer.Ordinal);
        foreach (var name in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber,
                     ClusterFixtureProtocol.NodeCount).Select(ClusterFixtureProtocol.NodeName))
        {
            var inspection = await ContainerRuntimeDocker.InspectAsync(names[name], cancellationToken)
                .ConfigureAwait(false);
            RequireInspection(inspection, context, RunningState);
            result.Add(name, inspection);
        }
        return result;
    }

    internal static async Task<IReadOnlyDictionary<string, ContainerRuntimeInspection>>
        VerifyStoppedAsync(IReadOnlyDictionary<string, string> names,
            IReadOnlyDictionary<string, ContainerRuntimeInspection> started,
            NativeCoverageRf3FixtureContext context, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ContainerRuntimeInspection>(StringComparer.Ordinal);
        foreach (var name in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber,
                     ClusterFixtureProtocol.NodeCount).Select(ClusterFixtureProtocol.NodeName))
        {
            var inspection = await ContainerRuntimeDocker.InspectAsync(names[name], cancellationToken)
                .ConfigureAwait(false);
            RequireInspection(inspection, context, ExitedState);
            if (inspection.Id != started[name].Id || inspection.ImageId != started[name].ImageId)
            {
                throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
            }
            result.Add(name, inspection);
        }
        return result;
    }

    private static void RequireInspection(ContainerRuntimeInspection inspection,
        NativeCoverageRf3FixtureContext context, string state)
    {
        if (inspection.State != state || inspection.ConfigImage != context.ImageReference
            || inspection.ImageId != context.ImageId)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
    }
}
