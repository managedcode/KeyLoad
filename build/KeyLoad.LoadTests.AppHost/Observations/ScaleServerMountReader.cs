using System.Globalization;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class ScaleServerMountReader
{
    private const string Docker = "docker", Stat = "stat", Sudo = "sudo", NonInteractive = "--non-interactive";
    private const string Context = "context", Inspect = "inspect", Format = "--format";
    private const string ContextFormat = "{{.Endpoints.docker.Host}}", UnixPrefix = "unix://";
    private const string FileSystem = "--file-system", StatFormat = "%T|%S|%b|%a", EndOptions = "--";
    private const char MountSeparator = ';', FieldSeparator = '~', StatSeparator = '|';
    private const int First = 0, Source = 0, Target = 1, MountType = 2, MountFields = 3;
    private const int StatFields = 4, BlockSize = 1, Blocks = 2, AvailableBlocks = 3;

    internal static async Task<ScaleServerMount[]> ReadAsync(string text, string[] expectedTargets,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var expected = new HashSet<string>(expectedTargets, StringComparer.Ordinal);
        if (expected.Count != expectedTargets.Length || expectedTargets.Length > budget.Settings.MaxMounts
            || expected.Count == First)
        {
            return [];
        }
        try
        {
            if (!await IsLocalDockerAsync(budget, token))
            {
                return [];
            }
            return await ReadMountedFilesystemsAsync(text, expected, budget, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (InvalidDataException) { return []; }
        catch (FormatException) { return []; }
        catch (System.ComponentModel.Win32Exception) { return []; }
        catch (OverflowException) { return []; }
    }

    private static async Task<ScaleServerMount[]> ReadMountedFilesystemsAsync(string text,
        HashSet<string> expected, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var mounts = new List<ScaleServerMount>();
        foreach (var row in text.Split(MountSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = row.Split(FieldSeparator, StringSplitOptions.None);
            if (fields.Length != MountFields || !Path.IsPathFullyQualified(fields[Source])
                || !Path.IsPathFullyQualified(fields[Target]) || string.IsNullOrWhiteSpace(fields[MountType])
                || mounts.Count >= budget.Settings.MaxMounts || !observed.Add(fields[Target]))
            {
                return [];
            }
            var output = await ScaleServerResourceProcess.RunAsync(Sudo,
                [NonInteractive, EndOptions, Stat, FileSystem, Format, StatFormat, EndOptions, fields[Source]], budget, token);
            if (output is null || !TryReadMount(fields[Target], fields[MountType], output, out var mount))
            {
                return [];
            }
            mounts.Add(mount!);
        }
        return expected.IsSubsetOf(observed) ? mounts.ToArray() : [];
    }

    private static bool TryReadMount(string target, string mountType, string output, out ScaleServerMount? mount)
    {
        mount = null;
        var fields = output.Trim().Split(StatSeparator, StringSplitOptions.None);
        if (fields.Length != StatFields || string.IsNullOrWhiteSpace(fields[First])
            || !long.TryParse(fields[BlockSize], NumberStyles.None, CultureInfo.InvariantCulture, out var blockSize)
            || !long.TryParse(fields[Blocks], NumberStyles.None, CultureInfo.InvariantCulture, out var blocks)
            || !long.TryParse(fields[AvailableBlocks], NumberStyles.None, CultureInfo.InvariantCulture, out var available)
            || blockSize <= First || blocks <= First || available < First || available > blocks)
        {
            return false;
        }
        mount = new(target, mountType, fields[First], checked(blockSize * blocks), checked(blockSize * available));
        return true;
    }

    private static async Task<bool> IsLocalDockerAsync(ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        if (budget.Provenance.DockerHost is not null || budget.Provenance.DockerContext is not null)
        {
            return false;
        }
        var endpoint = await ScaleServerResourceProcess.RunAsync(Docker,
            [Context, Inspect, Format, ContextFormat], budget, token);
        return endpoint?.Trim().StartsWith(UnixPrefix, StringComparison.Ordinal) == true;
    }
}
