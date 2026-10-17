using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeDataCleanup
{
    private const string Runner = "comparisons";
    private const string VolumePrefix = "keyload-isolated-";
    private const string Shell = "/bin/sh";
    private const string OwnedMount = "/owned";
    private const string RemoveOwnedData = "rm -rf /owned/native /owned/reports";
    private const string Failure = "IsolatedNativeDataCleanupFailed";

    internal static async Task DeleteAsync(string root, ContainerResource[] containers)
    {
        var volumes = containers.SelectMany(item => item.Annotations.OfType<ContainerMountAnnotation>())
            .Where(item => item.Type == ContainerMountType.Volume).Select(item => item.Source).Distinct().ToArray();
        foreach (var volume in volumes)
        {
            if (volume is null || !volume.StartsWith(VolumePrefix, StringComparison.Ordinal))
            {
                throw new IOException(Failure);
            }
            await DockerAsync(["volume", "rm", "--", volume]);
        }
        if (!Directory.Exists(root))
        {
            return;
        }
        var runner = containers.Single(item => item.Name == Runner);
        if (!runner.TryGetContainerImageName(out var image))
        {
            throw new IOException(Failure);
        }
        await DockerAsync(["run", "--rm", "--pull", "never", "--network", "none", "--read-only", "--user", "0:0",
            "--cap-drop", "ALL", "--cap-add", "DAC_OVERRIDE", "--entrypoint", Shell,
            "--mount", $"type=bind,source={root},target={OwnedMount}", image!, "-c", RemoveOwnedData]);
        Directory.Delete(root, recursive: true);
    }

    private static async Task DockerAsync(string[] arguments)
    {
        _ = await IsolatedKeyLoadFaultRegressionDocker.RunAsync(arguments, CancellationToken.None);
    }
}
