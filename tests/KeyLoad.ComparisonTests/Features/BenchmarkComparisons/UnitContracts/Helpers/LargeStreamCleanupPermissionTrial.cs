using System.Runtime.Versioning;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

[SupportedOSPlatform("linux")]
internal sealed class LargeStreamCleanupPermissionTrial
{
    private const string HealthyCapture = "healthy.bin";
    private const string Accepted = "accepted-large";
    private static readonly string[] OriginalFiles =
        ["complete.bin", "complete.pid", "one-over.bin", "one-over.pid", "healthy.bin", "healthy.pid"];
    private const UnixFileMode OwnedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode RefusalMode = UnixFileMode.UserRead | UnixFileMode.UserExecute;

    private string? root;
    private Dictionary<string, (byte[] Bytes, UnixFileMode Mode)>? original;
    private bool faultArmed;
    private UnauthorizedAccessException? initiating;

    internal static Task RunAsync(bool retain, CancellationToken token)
        => new LargeStreamCleanupPermissionTrial().RunOwnedAsync(retain, token);

    private async Task RunOwnedAsync(bool retain, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux())
        { throw new PlatformNotSupportedException("This actual permission-fault control requires Linux."); }
        var failures = new List<Exception>();
        try
        { await ServerFailureObserver.ObserveAsync(() => BodyAsync(retain, token), failures).ConfigureAwait(false); }
        finally
        {
            if (root is not null && Directory.Exists(root))
            {
                ServerFailureObserver.Observe(() => File.SetUnixFileMode(root, OwnedMode), failures);
                ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task BodyAsync(bool retain, CancellationToken token)
    {
        UnauthorizedAccessException? surfaced = null;
        try
        {
            using var directory = new ImageBundleTestDirectory();
            root = directory.Root;
            try
            {
                original = await PrepareAsync(root, token).ConfigureAwait(false);
                File.SetUnixFileMode(root, RefusalMode);
                faultArmed = true;
                if (retain)
                { DenyWrite(root); }
            }
            catch (Exception)
            { directory.RetainFailureEvidence(); throw; }
        }
        catch (UnauthorizedAccessException error) when (faultArmed)
        { surfaced = error; }
        await Assert.That(surfaced).IsNotNull();
        if (retain)
        { await Assert.That(ReferenceEquals(initiating, surfaced)).IsTrue(); }
        else
        { await Assert.That(initiating).IsNull(); }
        await Assert.That(root is not null && Directory.Exists(root)).IsTrue();
        await Assert.That(File.GetUnixFileMode(root!)).IsEqualTo(RefusalMode);
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => Directory.Delete(root!, recursive: true));
        var actual = await CaptureAsync(root!, token).ConfigureAwait(false);
        await Assert.That(original is not null && original.Count == actual.Count
            && original.All(item => actual.TryGetValue(item.Key, out var bytes)
                && item.Value.Mode == bytes.Mode && item.Value.Bytes.AsSpan().SequenceEqual(bytes.Bytes))).IsTrue();
    }

    private static async Task<Dictionary<string, (byte[] Bytes, UnixFileMode Mode)>> PrepareAsync(
        string root, CancellationToken token)
    {
        await Assert.That(File.GetUnixFileMode(root)).IsEqualTo(OwnedMode);
        var result = await IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", IsolatedGitHubFileProgram.LargeSource,
                IsolatedAggregateNodeProcess.Module("isolated-github-stream.mjs"), root, "219259", "async"],
            token).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output.Trim()).IsEqualTo(Accepted);
        await Assert.That(result.Error).IsEmpty();
        var captured = await CaptureAsync(root, token).ConfigureAwait(false);
        await Assert.That(captured.Count).IsEqualTo(OriginalFiles.Length);
        await Assert.That(captured[HealthyCapture].Bytes.Length).IsGreaterThan(0);
        return captured;
    }

    private void DenyWrite(string root)
    {
        try
        { using var denied = File.Open(Path.Combine(root, HealthyCapture), FileMode.Open, FileAccess.Write); }
        catch (UnauthorizedAccessException error)
        { initiating = error; throw; }
        throw new InvalidOperationException("The native sealed-file permission refusal was not observed.");
    }

    private static async Task<Dictionary<string, (byte[] Bytes, UnixFileMode Mode)>> CaptureAsync(string root, CancellationToken token)
    {
        var entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).ToArray();
        if (entries.Length != OriginalFiles.Length || !entries.Order(StringComparer.Ordinal)
            .SequenceEqual(OriginalFiles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        { throw new InvalidOperationException("The original native stream fixture inventory changed."); }
        var files = new Dictionary<string, (byte[] Bytes, UnixFileMode Mode)>(StringComparer.Ordinal);
        foreach (var name in OriginalFiles)
        {
            token.ThrowIfCancellationRequested();
            var file = Path.Combine(root, name);
            if ((File.GetAttributes(file) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
                || new FileInfo(file).LinkTarget is not null)
            { throw new InvalidOperationException("The original native capture is not a regular owned file."); }
            files.Add(name, (await File.ReadAllBytesAsync(file, token).ConfigureAwait(false), File.GetUnixFileMode(file)));
        }
        return files;
    }
}
