using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Preserves exact authenticated base admission and closed native build/user/permission annotations before startup.</summary>
internal static class ReplicaIsolationModelAssertions
{
    private const int BuildArguments = 2;
    private const int NoBuildSecrets = 0;
    private const string DockerfileName = "Dockerfile";
    private const string User = "--user";
    private const string Label = "--label=";
    private const string GuidFormat = "D";

    internal static async Task VerifyAsync(DistributedApplication app, ReplicaIsolationBuildPlan plan,
        CancellationToken cancellationToken)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resources = model.Resources.OfType<ContainerResource>().ToArray();
        if (resources.Length != plan.Targets.Count)
        { throw new InvalidOperationException("The native fault model must retain exactly the admitted voter cohort."); }
        foreach (var target in plan.Targets)
        {
            var node = resources.Single(resource => resource.Name == target.ResourceName);
            var annotation = node.Annotations.OfType<DockerfileBuildAnnotation>().Single();
            if (annotation.Stage != ReplicaIsolationComposition.Stage || annotation.ContextPath != plan.ContextPath
                || annotation.DockerfilePath != Path.Combine(plan.ContextPath, DockerfileName)
                || annotation.BuildSecrets.Count != NoBuildSecrets || annotation.DockerfileFactory is not null || !annotation.HasEntrypoint
                || annotation.BuildArguments.Count != BuildArguments
                || annotation.BuildArguments[ReplicaIsolationComposition.BaseArgument] is not string original || original != plan.BaseImage
                || annotation.BuildArguments[ReplicaIsolationComposition.SourceArgument] is not string digest || digest != plan.DockerfileSha256
                || !node.TryGetContainerImageName(useBuiltImage: false, out var baseImage) || baseImage != plan.NativeBaseImage
                || !node.TryGetContainerImageName(useBuiltImage: true, out var builtImage) || builtImage != target.ImageReference)
            { throw new InvalidOperationException("The native fault build context or authenticated image identity changed before startup."); }
            var arguments = new List<object>();
            var context = new ContainerRuntimeArgsCallbackContext(arguments, cancellationToken);
            foreach (var callback in node.Annotations.OfType<ContainerRuntimeArgsCallbackAnnotation>())
            { await callback.Callback(context).ConfigureAwait(false); }
            object[] expected = [User, target.ServiceUser, ReplicaIsolationComposition.Capability,
                Label + ReplicaIsolationComposition.IncarnationLabel + "=" + plan.Incarnation.ToString(GuidFormat)];
            if (!arguments.SequenceEqual(expected))
            { throw new InvalidOperationException("Only the unchanged native service user and selected NET_ADMIN/identity label are admitted."); }
        }
    }
}
