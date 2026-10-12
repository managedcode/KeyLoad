using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns never-mounted local files for real diagnostic-profile admission regressions.</summary>
internal sealed class NativeMembershipDiagnosticFileScope
{
    private const string DataPrefix = "keyload-membership-file-test-";
    private const string Invalid = "RequestCqrsProbeConfigurationInvalid";
    private bool dataOwned;
    private NativeMembershipDiagnosticFileScope()
    { DataRoot = Path.Combine(Path.GetTempPath(), DataPrefix + Guid.NewGuid().ToString("N")); }

    internal string DataRoot { get; }
    internal NativeMembershipDiagnosticFixture Fixture { get; private set; } = null!;
    internal string Node(string node) => Path.Combine(Fixture.Root, node);
    internal string Owner(string node) => Path.Combine(Node(node), RequestCqrsProbeFixtureProtocol.OwnerFileName);

    private void Initialize()
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported); }
        if (Directory.Exists(DataRoot) || File.Exists(DataRoot))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnershipConflict); }
        Directory.CreateDirectory(DataRoot, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
        dataOwned = true;
        Fixture = NativeMembershipDiagnosticFixture.Create(DataRoot, Guid.NewGuid());
    }

    internal IReadOnlyDictionary<string, string> Validate()
    {
        var options = new RequestProbeFileOptions();
        if (!options.IsValid())
        { throw new OptionsValidationException(Options.DefaultName, typeof(RequestProbeFileOptions), [RequestProbeFileOptions.ValidationMessage]); }
        return RequestCqrsProbeProfilePaths.Validate(Fixture.Root, Fixture.SessionId, DataRoot,
            Options.Create(options), TwoRf3MembershipProtocol.Nodes,
            RequestCqrsProbeFixtureProtocol.Version, RequestCqrsProbeFixtureProtocol.OwnerKind);
    }

    internal async Task RequireRejectedAsync()
    {
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => Validate());
        await Assert.That(failure.Message).IsEqualTo(Invalid);
    }

    private void DeleteNeverMountedFiles()
    {
        var failures = new List<Exception>();
        if (Fixture is not null)
        { ServerFailureObserver.Observe(() => Directory.Delete(Fixture.Root, recursive: true), failures); }
        if (dataOwned)
        { ServerFailureObserver.Observe(() => { Directory.Delete(DataRoot, recursive: true); dataOwned = false; }, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task WithAsync(Func<NativeMembershipDiagnosticFileScope, Task> action)
    {
        var scope = new NativeMembershipDiagnosticFileScope();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() =>
        { scope.Initialize(); return action(scope); }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(scope.DeleteNeverMountedFiles, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
