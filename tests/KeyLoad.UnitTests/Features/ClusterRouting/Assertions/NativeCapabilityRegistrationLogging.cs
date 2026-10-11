using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks real native Aspire model callbacks without starting or qualifying a container.</summary>
internal static class NativeCapabilityRegistrationLogging
{
    private const string DefaultKey = "Logging__LogLevel__Default";
    private const string WorkerKey = "Logging__LogLevel__KeyLoad.Server.Features.ClusterRouting.PhysicalOwnerRegistrationWorker";

    internal static async Task ProveAsync(IDistributedApplicationBuilder builder,
        LocalDevelopmentContainerImage image, NativeDiscoveryOmissionProfile? profile)
    {
        var resources = Enumerable.Range(1, 6).Select(index =>
            builder.AddContainer("marker-node-" + index, image.Reference)).ToArray();
        foreach (var resource in resources)
        { ClusterResourceSettings.Apply(builder, resource, null); }
        profile?.Apply(resources);
        for (var index = 0; index < resources.Length; index++)
        {
            var environment = new Dictionary<string, object>(StringComparer.Ordinal);
            var context = new EnvironmentCallbackContext(builder.ExecutionContext,
                resources[index].Resource, environment);
            foreach (var callback in resources[index].Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
            { await callback.Callback(context).ConfigureAwait(false); }
            await Assert.That(environment[DefaultKey]).IsEqualTo("Warning");
            if (profile is not null && index < 3)
            { await Assert.That(environment[WorkerKey]).IsEqualTo("Information"); }
            else
            { await Assert.That(environment.ContainsKey(WorkerKey)).IsFalse(); }
        }
    }
}
