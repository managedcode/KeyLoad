using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Observes one actual native server and its configured, retained storage directory.</summary>
internal sealed record IsolatedKeyLoadFaultChecksNativeNode(ProjectResource Model)
{
    private const string DataDirectorySetting = "KeyLoad__DataDirectory";
    internal string Resource => Model.Name;

    internal static IsolatedKeyLoadFaultChecksNativeNode Read(ProjectResource resource, int index)
    {
        IsolatedKeyLoadFaultChecksProtocol.Require(resource.Name == IsolatedKeyLoadFaultChecksProtocol.Resource(index)
            && Path.GetFileName(resource.GetProjectMetadata().ProjectPath) == "KeyLoad.csproj");
        return new(resource);
    }

    internal async Task<IsolatedKeyLoadFaultChecksNativeIdentity> InspectAsync(DistributedApplication app, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(Model);
        var directory = environment[DataDirectorySetting];
        IsolatedKeyLoadFaultChecksProtocol.Require(Path.IsPathFullyQualified(directory) && Directory.Exists(directory));
        if (!app.ResourceNotifications.TryGetCurrentState(Resource, out var current)
            || current.Resource is not ProjectResource || current.Snapshot.State?.Text is not { } state)
        {
            throw new InvalidOperationException(IsolatedKeyLoadFaultChecksProtocol.Failure);
        }
        var value = current.Snapshot.Properties.Where(property =>
            property.Name == ProcessFaultCommandProtocol.ProcessIdProperty).Select(property => property.Value).SingleOrDefault();
        var pid = int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? (int?)parsed : null;
        NativeProcessEvidence? process = null;
        if (state == KnownResourceStates.Running)
        {
            process = await ProjectProcessEvidence.ReadAsync(app.ResourceNotifications, Resource,
                app.Services.GetRequiredService<IOptions<TestExecutionOptions>>(), token);
        }
        var authority = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory))));
        return new(Resource, pid, current.Snapshot.StartTimeStamp?.ToUniversalTime(), state,
            current.Snapshot.ExitCode, authority, process);
    }
}
