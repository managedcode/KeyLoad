using System.Text.Json;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerMountReader
{
    private const int ReadNonnegativeBoundaryValue = 0;

    private const string TargetField = "target";
    private const string Docker = "docker";
    private const string Findmnt = "findmnt";
    private const string UnixPrefix = "unix://";
    private const string MountSeparator = ";";
    private const string FieldSeparator = "~";
    private const string ContextFormat = "{{.Endpoints.docker.Host}}";
    private const string FindmntOutput = "TARGET,FSTYPE,SIZE,AVAIL";

    internal static async Task<ScaleServerMount[]> ReadAsync(string text, string[] expectedTargets,
        ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const int SingleFilesystemCount = 0;
        const int MountFieldCount = 3;
        const int FirstIndex = 0;
        const int ElementIndex = 2;
        const int SecondIndex = 1;

        token.ThrowIfCancellationRequested();
        var expected = new HashSet<string>(expectedTargets, StringComparer.Ordinal);
        if (expected.Count != expectedTargets.Length || expectedTargets.Length > budget.Settings.MaxMounts)
        {
            return [];
        }

        if (expected.Count == SingleFilesystemCount)
        {
            return [];
        }

        try
        {
            if (!await IsLocalDockerAsync(budget, token))
            {
                return [];
            }

            var observed = new HashSet<string>(StringComparer.Ordinal);
            var mounts = new List<ScaleServerMount>();
            foreach (var row in text.Split(MountSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = row.Split(FieldSeparator, StringSplitOptions.None);
                if (fields.Length != MountFieldCount || !Path.IsPathFullyQualified(fields[FirstIndex]) || fields[ElementIndex].Length == SingleFilesystemCount
                    || mounts.Count >= budget.Settings.MaxMounts || !expected.Contains(fields[SecondIndex])
                    || !observed.Add(fields[SecondIndex]))
                {
                    return [];
                }

                var mount = await ReadActualMountAsync(fields[FirstIndex], fields[SecondIndex], fields[ElementIndex], budget, token);
                if (mount is null)
                {
                    return [];
                }

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
        const string ArgumentsText = "context";
        const string IsLocalDockerAsyncArgumentsText = "inspect";

        if (budget.Provenance.DockerHost is not null || budget.Provenance.DockerContext is not null)
        {
            return false;
        }

        var endpoint = await ScaleServerResourceProcess.RunAsync(Docker,
            [ArgumentsText, IsLocalDockerAsyncArgumentsText, "--format", ContextFormat], token, budget, budget.Settings.MaxNativeOutputBytes);
        return endpoint?.Trim().StartsWith(UnixPrefix, StringComparison.Ordinal) == true;
    }

    private static async Task<ScaleServerMount?> ReadActualMountAsync(string source, string containerPath,
        string mountType, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const string ArgumentsText = "--json";
        const string ReadActualMountAsyncArgumentsText = "--bytes";
        const string PropertyNameText = "filesystems";
        const int SingleFilesystemCount = 1;
        const int IndexValue = 0;
        const string ReadActualMountAsyncPropertyNameText = "fstype";
        const int BoundaryValue = 0;

        string canonicalSource;
        try
        { canonicalSource = Path.GetFullPath(source); }
        catch (ArgumentException) { return null; }
        if (!Directory.Exists(canonicalSource) && !File.Exists(canonicalSource))
        {
            return null;
        }

        var json = await ScaleServerResourceProcess.RunAsync(Findmnt,
            [ArgumentsText, ReadActualMountAsyncArgumentsText, "--target", canonicalSource, "--output", FindmntOutput], token, budget,
            budget.Settings.MaxNativeOutputBytes);
        if (json is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(PropertyNameText, out var rows)
                || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != SingleFilesystemCount)
            {
                return null;
            }

            var row = rows[IndexValue];
            if (!HasExactMountFields(row))
            {
                return null;
            }

            var target = Path.GetFullPath(row.GetProperty(TargetField).GetString() ?? string.Empty);
            var fileSystem = row.GetProperty(ReadActualMountAsyncPropertyNameText).GetString();
            var capacity = ReadNonnegative(row.GetProperty("size"));
            var available = ReadNonnegative(row.GetProperty("avail"));
            if (!IsPathWithin(canonicalSource, target) || string.IsNullOrWhiteSpace(fileSystem)
                || capacity <= BoundaryValue || available < BoundaryValue || available > capacity)
            {
                return null;
            }

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
        const string OtherText = "avail";
        const string HasExactMountFieldsOtherText = "fstype";

        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var names = value.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
        return names.SequenceEqual([OtherText, HasExactMountFieldsOtherText, "size", "target"], StringComparer.Ordinal);
    }

    private static long ReadNonnegative(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= ReadNonnegativeBoundaryValue
            ? number : throw new FormatException();

    private static bool IsPathWithin(string path, string directory)
    {
        var root = Path.GetPathRoot(directory);
        var prefix = directory == root ? directory : directory + Path.DirectorySeparatorChar;
        return path == directory || path.StartsWith(prefix, StringComparison.Ordinal);
    }
}
