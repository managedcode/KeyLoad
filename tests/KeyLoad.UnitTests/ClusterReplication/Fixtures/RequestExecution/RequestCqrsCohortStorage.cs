using KeyLoad.Clustering;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

/// <summary>Owns the same fixed voters' directories until every original native handle has joined.</summary>
internal sealed class RequestCqrsCohortStorage : IDisposable
{
    private const int NoFailures = 0;
    private const string Prefix = "keyload-native-cohort-";
    private const string GuidFormat = "N";
    private const string Retained = "The original native cohort storage acquisition did not complete; its directories remain owned.";
    private readonly string directory = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));
    private bool acquisitionFailed;

    internal RequestCqrsCohortNativeNode CreateNode(string voter, ILocalSiloDetails local, IServiceProvider services,
        ReplicaConfiguration configuration, ReadOnlyMemory<byte> secret, RequestCqrsCohortCheckpointScenario? checkpoints,
        ReplicaBarrierDeadlineScenario? barrierDeadline, ReadOnlyMemory<byte>? canonicalKey,
        global::KeyLoad.UnitTests.Features.ClusterRouting.RequestCqrsHostedProfile? hosted)
    {
        try
        {
            var voterDirectory = Path.Combine(directory, voter);
            Directory.CreateDirectory(voterDirectory);
            return new(voter, local, services, configuration, secret, checkpoints, barrierDeadline, canonicalKey,
                hosted, voterDirectory);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            acquisitionFailed = true;
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            acquisitionFailed = true;
            throw;
        }
    }

    internal void RequireRestartJoined(List<Exception> failures)
    {
        if (failures.Count > NoFailures)
        {
            acquisitionFailed = true;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose()
    {
        if (acquisitionFailed)
        {
            throw new InvalidOperationException(Retained);
        }
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
