using System.Diagnostics;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsOfflineProcessAssertions
{
    internal static async Task AssertExitedAsync(Process process)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        await Assert.That(process.HasExited).IsTrue();
    }
}
