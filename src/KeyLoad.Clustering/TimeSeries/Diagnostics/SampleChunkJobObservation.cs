using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

internal static class SampleChunkJobObservation
{
    internal static void Returned(IServiceProvider services, Guid commandId, string actualJobId,
        string originalMetadata)
        => SampleChunkJobDiagnostics.ProviderReturned(services.GetRequiredService<ILogger<SampleChunkCoordinatorGrain>>(),
            commandId, actualJobId, Digest(originalMetadata));

    internal static void Executing(IServiceProvider services, Guid commandId, string actualJobId,
        string originalMetadata)
        => SampleChunkJobDiagnostics.ProviderExecuting(services.GetRequiredService<ILogger<SampleChunkCoordinatorGrain>>(),
            commandId, actualJobId, Digest(originalMetadata));

    private static string Digest(string originalMetadata)
    {
        var original = Convert.FromBase64String(originalMetadata);
        return Convert.ToHexStringLower(SHA256.HashData(original));
    }
}
