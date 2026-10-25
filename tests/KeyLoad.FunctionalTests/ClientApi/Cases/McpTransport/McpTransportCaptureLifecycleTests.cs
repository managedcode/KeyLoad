using KeyLoad.FunctionalTests.TestInfrastructure;
using KeyLoad.Server;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>AC-CLIENT-008: execution captures remain enabled across real provider/listener disposal and recreation.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class McpTransportCaptureLifecycleTests
{
    private const int CaptureLifetimes = 2;
    private const string MethodCanary = "private-lifecycle-method-canary";

    /// <summary>Two independently owned execution capture lifetimes preserve closed categories and privacy.</summary>
    [Test]
    public async Task ProviderCaptureCanBeDisposedAndReenabled()
    {
        for (var lifetime = 0; lifetime < CaptureLifetimes; lifetime++)
        {
            using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
            using var capture = new McpTransportEventSourceCapture();
            var logger = factory.CreateLogger(nameof(McpTransportDiagnosticsTests));
            await Assert.That(logger.IsEnabled(LogLevel.Warning)).IsTrue();
            var error = Errors.Fail(ErrorCode.Validation, McpTransportProtocol.InvalidTransport);
            error.Data[McpTransportDiagnostics.StageMetadataKey] = McpTransportStage.BodyMethodMismatch;
            McpTransportDiagnostics.Log(logger, error, McpTransportGuardTestData.Headers(RequestMethods.ToolsCall, null), UnitMcpOptions.Execution());
            McpTransportDiagnostics.Log(logger, error, McpTransportGuardTestData.Headers(MethodCanary, null), UnitMcpOptions.Execution());
            await Assert.That(capture.Text).Contains(nameof(McpTransportMethodCategory.ToolsCall));
            await Assert.That(capture.Text).Contains(nameof(McpTransportMethodCategory.Other));
            await Assert.That(capture.Text).Contains(nameof(McpTransportStage.BodyMethodMismatch));
            await Assert.That(capture.Text).DoesNotContain(MethodCanary);
            await Assert.That(capture.Text).DoesNotContain(nameof(KeyLoadException));
        }
    }
}
