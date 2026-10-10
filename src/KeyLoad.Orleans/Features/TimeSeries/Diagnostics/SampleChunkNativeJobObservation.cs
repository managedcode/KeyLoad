using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

internal static class SampleChunkNativeJobObservation
{
    internal static void Returned(IServiceProvider services, Guid commandId, string actualJobId,
        string originalMetadata, int maximumBytes)
        => SampleChunkJobDiagnostics.ProviderReturned(services.GetRequiredService<ILogger<SampleChunkCoordinatorGrain>>(),
            commandId, actualJobId, Digest(originalMetadata, maximumBytes));

    internal static void Executing(IServiceProvider services, Guid commandId, string actualJobId,
        string originalMetadata, int maximumBytes)
        => SampleChunkJobDiagnostics.ProviderExecuting(services.GetRequiredService<ILogger<SampleChunkCoordinatorGrain>>(),
            commandId, actualJobId, Digest(originalMetadata, maximumBytes));

    private static string Digest(string originalMetadata, int maximumBytes)
    {
        var original = Convert.FromBase64String(originalMetadata);
        if (original.Length > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkJobProtocol.Exhausted); }
        return Convert.ToHexStringLower(SHA256.HashData(original));
    }
}
