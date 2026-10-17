using System.Runtime.InteropServices;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Lets Aspire own one SDK image publisher before starting its dependent server containers.</summary>
internal sealed record SdkContainerImage(string Repository, string Tag, IResourceBuilder<ExecutableResource> Publisher)
{
    private const string ImageResourceName = "keyload-server-image";
    private const string ServerRepository = "keyload/server";
    private const string Command = "dotnet";
    private const string RepositoryRoot = "../..";
    private const string ServerProject = "src/KeyLoad.Server/KeyLoad.Server.csproj";
    private const string PublishCommand = "publish";
    private const string DisableBuildServersArgument = "--disable-build-servers";
    private const string ContainerTarget = "/t:PublishContainer";
    private const string ConfigurationArgument = "--configuration";
    private const string ReleaseConfiguration = "Release";
    private const string WarningsAsErrorsArgument = "-warnaserror";
    private const string RuntimeArgument = "--runtime";
    private const string ArtifactsArgument = "--artifacts-path";
    private const string ArtifactsDirectory = "artifacts/containers";
    private const string SingleBuildWorkerArgument = "-m:1";
    private const string ArmRuntime = "linux-arm64";
    private const string X64Runtime = "linux-x64";
    private const string ImageTagArgument = "-p:ContainerImageTag=";
    private const string GuidFormat = "N";
    private const string UnsupportedArchitecture = "The container publisher requires an ARM64 or x64 host.";

    internal static SdkContainerImage Add(IDistributedApplicationBuilder builder, Guid incarnation)
    {
        var runtime = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => ArmRuntime,
            Architecture.X64 => X64Runtime,
            _ => throw new PlatformNotSupportedException(UnsupportedArchitecture)
        };
        var tag = incarnation.ToString(GuidFormat);
        var root = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, RepositoryRoot));
        var publisher = builder.AddExecutable(ImageResourceName, Command, root,
            PublishCommand, ServerProject, ContainerTarget, DisableBuildServersArgument, ConfigurationArgument, ReleaseConfiguration,
            WarningsAsErrorsArgument, SingleBuildWorkerArgument, RuntimeArgument, runtime,
            ArtifactsArgument, Path.Combine(root, ArtifactsDirectory, tag), ImageTagArgument + tag);
        return new(ServerRepository, tag, publisher);
    }

    internal IResourceBuilder<ContainerResource> AddNode(IDistributedApplicationBuilder builder, string name)
        => builder.AddContainer(name, Repository, Tag).WaitForCompletion(Publisher);
}
