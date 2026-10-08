using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ReplicaIsolationAdmissionDiagnostics
{
    internal static void Throw(ReplicaIsolationAdmissionMismatch mismatch)
    {
        var failures = new List<Exception>
        {
            new InvalidOperationException("The inspected namespace is not the exact admitted non-root fault cohort.")
        };
        ServerFailureObserver.Observe(() => Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            kind = "OwnedRf3NamespaceAdmission",
            firstMismatch = mismatch.ToString()
        })), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
