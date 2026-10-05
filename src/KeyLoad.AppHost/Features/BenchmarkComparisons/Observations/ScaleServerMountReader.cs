using System.Text.Json;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerMountReader
{
    private const string Docker = "docker";
    private const string Findmnt = "findmnt";
    private const string DockerHostEnvironment = "DOCKER_HOST";
    private const string DockerContextEnvironment = "DOCKER_CONTEXT";
    private const string UnixPrefix = "unix://";
    private const string MountSeparator = ";";
    private const string FieldSeparator = "~";
    private const int MaxNativeOutputBytes = 4096;
    private const string ContextFormat = "{{.Endpoints.docker.Host}}";
    private const string FindmntOutput = "TARGET,FSTYPE,SIZE,AVAIL";

    internal static async Task<ScaleServerMount[]> ReadAsync(string text, string[] expectedTargets,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var expected = new HashSet<string>(expectedTargets, StringComparer.Ordinal);
        if (expected.Count != expectedTargets.Length || expectedTargets.Length > ScaleServerResourceBounds.MaxMounts)
            return [];
        if (expected.Count == 0) return [];
        try
        {
            if (!await IsLocalDockerAsync(budget, token)) return [];
            var observed = new HashSet<string>(StringComparer.Ordinal);
            var mounts = new List<ScaleServerMount>();
            foreach (var row in text.Split(MountSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = row.Split(FieldSeparator, StringSplitOptions.None);
                if (fields.Length != 3 || !Path.IsPathFullyQualified(fields[0]) || fields[2].Length == 0
                    || mounts.Count >= ScaleServerResourceBounds.MaxMounts || !expected.Contains(fields[1])
                    || !observed.Add(fields[1])) return [];
                var mount = await ReadActualMountAsync(fields[0], fields[1], fields[2], budget, token);
                if (mount is null) return [];
                mounts.Add(mount);
            }
            return observed.SetEquals(expected) ? mounts.ToArray() : [];
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (InvalidDataException) { return []; }
        catch (FormatException) { return []; }
        catch (System.ComponentModel.Win32Exception) { return []; }
        catch (OverflowException) { return []; }
    }

    private static async Task<bool> IsLocalDockerAsync(ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable(DockerHostEnvironment) is not null
            || Environment.GetEnvironmentVariable(DockerContextEnvironment) is not null) return false;
        var endpoint = await ScaleServerResourceProcess.RunAsync(Docker,
            ["context", "inspect", "--format", ContextFormat], token, budget, MaxNativeOutputBytes);
        return endpoint?.Trim().StartsWith(UnixPrefix, StringComparison.Ordinal) == true;
    }

    private static async Task<ScaleServerMount?> ReadActualMountAsync(string source, string containerPath,
        string mountType, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        string canonicalSource;
        try { canonicalSource = Path.GetFullPath(source); }
        catch (ArgumentException) { return null; }
        if (!Directory.Exists(canonicalSource) && !File.Exists(canonicalSource)) return null;
        var json = await ScaleServerResourceProcess.RunAsync(Findmnt,
            ["--json", "--bytes", "--target", canonicalSource, "--output", FindmntOutput], token, budget,
            MaxNativeOutputBytes);
        if (json is null) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("filesystems", out var rows)
                || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 1) return null;
            var row = rows[0];
            if (!HasExactMountFields(row)) return null;
            var target = Path.GetFullPath(row.GetProperty("target").GetString() ?? string.Empty);
            var fileSystem = row.GetProperty("fstype").GetString();
            var capacity = ReadNonnegative(row.GetProperty("size"));
            var available = ReadNonnegative(row.GetProperty("avail"));
            if (!IsPathWithin(canonicalSource, target) || string.IsNullOrWhiteSpace(fileSystem)
                || capacity <= 0 || available < 0 || available > capacity) return null;
            return new ScaleServerMount(containerPath, mountType, fileSystem, capacity, available);
        }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (FormatException) { return null; }
        catch (OverflowException) { return null; }
    }

    private static bool HasExactMountFields(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = value.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
        return names.SequenceEqual(["avail", "fstype", "size", "target"], StringComparer.Ordinal);
    }

    private static long ReadNonnegative(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0
            ? number : throw new FormatException();

    private static bool IsPathWithin(string path, string directory)
    {
        var root = Path.GetPathRoot(directory);
        var prefix = directory == root ? directory : directory + Path.DirectorySeparatorChar;
        return path == directory || path.StartsWith(prefix, StringComparison.Ordinal);
    }
}
