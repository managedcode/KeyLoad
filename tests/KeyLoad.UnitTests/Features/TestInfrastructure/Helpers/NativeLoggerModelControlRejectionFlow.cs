using Aspire.Hosting;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.TestInfrastructure.Helpers;

internal static class NativeLoggerModelControlRejectionFlow
{
    internal static async Task AssertRejectedAsync(string root, string name, string[] overrides,
        bool includeImage, string expectedRejectionMessage,
        Func<string, bool, string[], IDistributedApplicationBuilder> createBuilder)
    {
        var failures = new List<Exception>();
        IDistributedApplicationBuilder? builder = null;
        var resourceCount = 0;
        try
        {
            ServerFailureObserver.Observe(() =>
            {
                builder = createBuilder(root, includeImage, overrides);
                resourceCount = builder.Resources.Count;
            }, failures);
            if (builder is { } actualBuilder)
            {
                await ServerFailureObserver.ObserveAsync(
                    () => AssertAdmissionRejectedAsync(actualBuilder, name, expectedRejectionMessage), failures)
                    .ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(
                    async () => { await Assert.That(actualBuilder.Resources.Count).IsEqualTo(resourceCount).Because(name); }, failures)
                    .ConfigureAwait(false);
            }
            await ServerFailureObserver.ObserveAsync(
                async () => { await Assert.That(Directory.Exists(root) || File.Exists(root)).IsFalse().Because(name); }, failures).ConfigureAwait(false);
        }
        finally
        {
            ServerFailureObserver.Observe(() => DeleteOwnedRootIfPresent(root), failures);
        }
        await ServerFailureObserver.ObserveAsync(
            async () => { await Assert.That(Directory.Exists(root) || File.Exists(root)).IsFalse().Because(name); }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AssertAdmissionRejectedAsync(IDistributedApplicationBuilder builder, string name,
        string expectedRejectionMessage)
    {
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => KeyLoadAppHostApplication.AddKeyLoad(builder));
        await Assert.That(failure.Message).IsEqualTo(expectedRejectionMessage).Because(name);
    }

    private static void DeleteOwnedRootIfPresent(string root)
    {
        if (Directory.Exists(root))
        { Directory.Delete(root, recursive: true); }
        else if (File.Exists(root))
        { File.Delete(root); }
    }
}
