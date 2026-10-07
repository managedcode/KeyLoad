using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class TwoRf3MembershipImageModelTests
{
    private const string ControlledImageArgument = "--KeyLoad:ContainerImages:Server=";
    private const string ControlledImageReference =
        "registry.invalid/keyload/server:controlled-model@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string AlteredDigest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string RootPrefix = "membership-image-model-";
    private const string GuidFormat = "N";
    private const string ImageMismatchExpected = "The controlled image model should have been rejected.";
    private const string ModelMutation = "The image verifier changed an Aspire resource annotation.";
    private const string RootOccupied = "The unique image-model data root is already occupied.";

    [Test]
    public async Task AcMembership006RejectsChangedModelDigestWithoutMutationAndAcceptsRestoredModel()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, timeout.Token);
        await BuildAndVerifyModelAsync(deadline.Token).ConfigureAwait(false);
    }

    private static async Task BuildAndVerifyModelAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var root = CreateRootPath();
        var rootOwned = false;
        DistributedApplication? application = null;
        var applicationDisposed = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (Directory.Exists(root) || File.Exists(root))
            {
                throw new IOException(RootOccupied);
            }
            Directory.CreateDirectory(root);
            rootOwned = true;
            MakePrivateDirectory(root);
            _ = await NodeEpochRf3Profile.CreatePriorAsync(root, cancellationToken).ConfigureAwait(false);
            var args = new[]
            {
                TwoRf3MembershipProtocol.DataRootPrefix + root,
                TwoRf3MembershipProtocol.EphemeralArgument,
                TwoRf3MembershipProtocol.ProfileArgument,
                ControlledImageArgument + ControlledImageReference
            };
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
                cancellationToken).ConfigureAwait(false);
            application = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
            await RejectUnchangedThenAcceptAsync(application, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (application is not null)
        {
            var before = failures.Count;
            await ServerFailureObserver.ObserveAsync(() => application.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
            applicationDisposed = failures.Count == before;
        }
        if (rootOwned && (application is null || applicationDisposed))
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RejectUnchangedThenAcceptAsync(DistributedApplication application,
        CancellationToken cancellationToken)
    {
        await TwoRf3MembershipImageAssertions.VerifyAsync(application, ControlledImageReference, cancellationToken)
            .ConfigureAwait(false);
        var resources = GetNodes(application);
        var healthy = Capture(resources);
        var target = resources.Single(resource => resource.Name == TwoRf3MembershipProtocol.Node1);
        var image = target.Annotations.OfType<ContainerImageAnnotation>().Single();
        var originalDigest = image.SHA256
            ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch);
        image.SHA256 = AlteredDigest;
        var before = Capture(resources);
        var rejected = false;
        try
        {
            await TwoRf3MembershipImageAssertions.VerifyAsync(application, ControlledImageReference,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception) when (exception.Message == TwoRf3MembershipProtocol.ImageMismatch)
        { rejected = true; }
        if (!rejected)
        { throw new InvalidOperationException(ImageMismatchExpected); }
        AssertUnchanged(resources, before);
        image.SHA256 = originalDigest;
        await TwoRf3MembershipImageAssertions.VerifyAsync(application, ControlledImageReference, cancellationToken)
            .ConfigureAwait(false);
        AssertUnchanged(resources, healthy);
    }

    private static ContainerResource[] GetNodes(DistributedApplication application)
        => application.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().Where(resource => TwoRf3MembershipProtocol.Nodes.Contains(resource.Name,
                StringComparer.Ordinal)).ToArray();

    private static (string Name, string? Registry, string Image, string? Tag, string? Digest)[] Capture(
        ContainerResource[] resources)
        => resources.Select(resource =>
        {
            var image = resource.Annotations.OfType<ContainerImageAnnotation>().Single();
            return (resource.Name, image.Registry, image.Image, image.Tag, image.SHA256);
        }).ToArray();

    private static void AssertUnchanged(ContainerResource[] resources,
        (string Name, string? Registry, string Image, string? Tag, string? Digest)[] expected)
    {
        var actual = Capture(resources);
        if (resources.Length != TwoRf3MembershipProtocol.NodeCount || !expected.SequenceEqual(actual))
        { throw new InvalidOperationException(ModelMutation); }
    }

    private static string CreateRootPath()
        => Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, "artifacts",
            "qualification", RootPrefix + Guid.NewGuid().ToString(GuidFormat));

    private static void MakePrivateDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }
}
