using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.ComparisonHost.BenchmarkOrchestration;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

internal sealed class NativeDatabaseResourceObservation
{
    private const string FileName = "resource-observations.json";
    private const char FieldSeparator = '|';
    private const char MountSeparator = ';', MountFieldSeparator = '~';
    private const int MountDescriptor = 5, MountTarget = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly DistributedApplication app;
    private readonly string root;
    private readonly ContainerResource[] resources;
    private readonly ScaleServerResourceEvidenceCollector collector;
    private readonly Task observation;
    private readonly List<TargetObservation> targets = [];

    internal NativeDatabaseResourceObservation(DistributedApplication app, string root, CancellationToken token)
    {
        this.app = app;
        this.root = root;
        var settings = app.Services.GetRequiredService<LoadTestClientSettings>();
        resources = [.. app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().Where(resource => settings.Resources.Contains(resource.Name))];
        collector = app.Services.GetRequiredService<ScaleServerResourceEvidenceCollector>();
        observation = collector.StartAsync(resources,
            ready => BenchmarkSessionClientRuntime.WaitForReadinessAsync(app, ready), token);
    }

    internal async Task VerifyReadyAsync(CancellationToken token)
    {
        if (OperatingSystem.IsLinux())
        {
            await CaptureHostMountsAsync(token);
            await collector.WaitForInitialSamplesAsync(token);
        }
        foreach (var resource in resources)
        {
            var identity = ScaleServerResourceIdentity.Read(app.ResourceNotifications, resource);
            var budget = new ScaleServerResourceSampleBudget(app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>(),
                app.Services.GetRequiredService<IOptions<BenchmarkProvenanceOptions>>(), timeProvider: TimeProvider.System);
            var inspected = (await ScaleServerResourceProcess.InspectAsync(identity, budget, token)).Value!;
            await Assert.That(inspected).IsNotNull();
            var expected = resource.Annotations.OfType<ContainerMountAnnotation>().Where(mount => !mount.IsReadOnly)
                .Select(mount => mount.Target).ToArray();
            var descriptor = inspected.Split(FieldSeparator)[MountDescriptor];
            var actual = descriptor.Split(MountSeparator, StringSplitOptions.RemoveEmptyEntries).Select(row => row.Split(MountFieldSeparator)[MountTarget]).ToArray();
            await Assert.That(expected.All(target => actual.Contains(target, StringComparer.Ordinal))).IsTrue();
            await Assert.That(actual.Length).IsGreaterThan(0);
            await Assert.That(actual.Length).IsLessThanOrEqualTo(budget.Settings.MaxMounts);
            await Assert.That(actual.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(actual.Length);
            await VerifyMountIdentityAsync(descriptor);
            ScaleServerMount[] mounts = [];
            if (OperatingSystem.IsLinux())
            {
                mounts = await ScaleServerMountReader.ReadAsync(descriptor, expected, budget, token);
                await Assert.That(mounts.Length).IsEqualTo(actual.Length);
            }
            targets.Add(new(resource.Name, identity, actual, mounts));
        }
    }

    private static async Task VerifyMountIdentityAsync(string descriptor)
    {
        const string Changed = "-changed";
        const int First = 0, Fields = 3;
        var rows = descriptor.Split(MountSeparator, StringSplitOptions.RemoveEmptyEntries);
        var identity = ScaleServerMountReader.NormalizeDescriptor(descriptor);
        var reordered = string.Join(MountSeparator, rows.Reverse());
        await Assert.That(ScaleServerMountReader.NormalizeDescriptor(reordered)).IsEqualTo(identity);
        for (var field = First; field < Fields; field++)
        {
            var changed = rows.ToArray();
            var values = changed[First].Split(MountFieldSeparator);
            values[field] += Changed;
            changed[First] = string.Join(MountFieldSeparator, values);
            await Assert.That(ScaleServerMountReader.NormalizeDescriptor(string.Join(MountSeparator, changed)))
                .IsNotEqualTo(identity);
        }
    }

    private async Task CaptureHostMountsAsync(CancellationToken token)
    {
        const string MountInfoFile = "host-mountinfo.txt";
        var settings = app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>();
        var budget = new ScaleServerResourceSampleBudget(settings,
            app.Services.GetRequiredService<IOptions<BenchmarkProvenanceOptions>>(), timeProvider: TimeProvider.System);
        var mounts = (await BoundedText.ReadAsync(ScaleServerCgroupHierarchy.MountInfoPath,
            settings.Value.MaxHardwareBytes, budget, token)).Value;
        await Assert.That(mounts).IsNotNull();
        await File.WriteAllTextAsync(Path.Combine(root, MountInfoFile), mounts!, token);
    }

    internal async Task CompleteAsync()
    {
        await collector.CompleteAsync(observation);
        var snapshot = collector.ReadObservations();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { observations = snapshot, targets }, Json);
        await Assert.That(bytes.Length).IsLessThanOrEqualTo(app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>().Value.MaxSidecarBytes);
        await File.WriteAllBytesAsync(Path.Combine(root, FileName), bytes);
        if (OperatingSystem.IsLinux())
        {
            await Assert.That(snapshot.Qualified).IsTrue().Because(string.Join(FieldSeparator, snapshot.MissingEvidence));
            await Assert.That(snapshot.Containers.Length).IsEqualTo(resources.Length);
        }
    }

    private sealed record TargetObservation(string Resource, string ContainerId, string[] WritableMountTargets, ScaleServerMount[] WritableMounts);
}
