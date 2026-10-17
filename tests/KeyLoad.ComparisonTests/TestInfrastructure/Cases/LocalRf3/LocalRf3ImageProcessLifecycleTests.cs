using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.TestInfrastructure.Execution;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class LocalRf3ImageProcessLifecycleTests
{
    private const string Tag = "local-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Receipt = "TestResults/rf3/local-images/image-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json";
    private const string OverflowMessage = "Local RF3 image cleanup output exceeded its bound.";

    [Test]
    public async Task CleanupStopsActualNodeProcessWhenBoundedReaderOverflowsBeforeExit()
    {
        await LocalRf3ImageTestDirectory.RunAsync("keyload-local-image-cleanup-", async root =>
        {
            var script = Path.Combine(root, "overflow.mjs");
            var terminated = Path.Combine(root, "terminated");
            await File.WriteAllTextAsync(script, OverflowProgram(terminated));
            var execution = new LocalRf3ImageExecution(root, Tag, Receipt);
            var cleanup = new LocalRf3ImageCleanup(execution, script,
                AppHostOptionsRegistration.BindTestExecution(new ConfigurationBuilder().Build()));
            var failure = await CaptureFailureAsync(cleanup.CleanupAsync(CancellationToken.None));

            await Assert.That(ContainsMessage(failure, OverflowMessage)).IsTrue();
            await Assert.That(File.Exists(terminated)).IsTrue();
        });
    }

    private static string OverflowProgram(string terminated)
        => "import { writeFileSync } from 'node:fs';\n"
            + "process.on('SIGTERM', () => { writeFileSync(" + JsonSerializer.Serialize(terminated)
            + ", 'term'); process.exit(0); });\n"
            + "process.stdout.write('x'.repeat(8192));\nsetInterval(() => {}, 1000);\n";

    private static Task<Exception?> CaptureFailureAsync(Task operation)
        => KeyLoad.AppHost.Features.TestInfrastructure.Processes.OwnedProcessFailureObserver.CaptureAsync(operation);

    private static bool ContainsMessage(Exception? exception, string message)
        => exception is not null && (exception.Message == message
            || exception is AggregateException aggregate
                && aggregate.InnerExceptions.Any(inner => ContainsMessage(inner, message)));
}
