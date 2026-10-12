using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.BlobStorage;

internal static class NativeBlobWireProbeProfile
{
    internal const string Section = "KeyLoadTests:NativeBlobWireProbe";
    private const string Mount = "/native-blob-wire-probes";
    private const string RootEnvironment = "KeyLoad__NativeBlobWireProbe__Root";
    private const string SessionEnvironment = "KeyLoad__NativeBlobWireProbe__SessionId";
    internal const string Invalid = "NativeBlobWireProfileInvalid";
    private const string DirectoryName = "native-blob-wire-probes";
    private const int FirstSourceMember = 0;
    private const int SourceMembers = 3;
    private const int TotalMembers = 6;

    internal static void Apply(IDistributedApplicationBuilder builder, string dataRoot,
        IReadOnlyList<IResourceBuilder<ContainerResource>> resources)
    {
        var control = AppHostOptionsRegistration.Get(builder).Control.Value;
        var selected = control.NativeBlobWireProbe;
        if (selected is null) { return; }
        var root = selected.Root;
        var session = selected.SessionId;
        var expected = Path.Combine(Path.GetFullPath(dataRoot), DirectoryName);
        if (!control.Ephemeral || !control.TwoRf3 || control.BenchmarksEnabled || control.ProtocolCohortEnabled
            || !control.RemoteDocumentReads || !control.RemotePartitionQueries || !control.ProtectedDocumentMovement
            || control.RequestProbe is null || session != control.RequestProbe.SessionId
            || string.IsNullOrEmpty(root) || !Path.IsPathFullyQualified(root)
            || Path.GetFullPath(root) != expected || resources.Count != TotalMembers)
        { throw new InvalidOperationException(Invalid); }
        var directory = new DirectoryInfo(root);
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != default
            || OperatingSystem.IsWindows() || File.GetUnixFileMode(root)
                != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
        { throw new InvalidOperationException(Invalid); }
        for (var index = FirstSourceMember; index < SourceMembers; index++)
        {
            resources[index].WithBindMount(root, Mount)
                .WithEnvironment(RootEnvironment, Mount).WithEnvironment(SessionEnvironment, session);
        }
    }
}
