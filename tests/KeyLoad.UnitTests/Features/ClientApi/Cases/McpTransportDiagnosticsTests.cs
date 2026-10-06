using KeyLoad.Server;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-CLIENT-008: real guard rejections carry only closed, private diagnostic metadata.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class McpTransportDiagnosticsTests
{
    private const string MethodCanary = "private-method-canary";
    private const string HeaderCanary = "private-header-canary";
    private const string MetadataCanary = "private-metadata-canary";
    private const string PrivateHeader = "X-Private-Diagnostic";
    private const string CategoryOther = "Other";
    private const string CategoryToolsCall = "ToolsCall";

    /// <summary>Every concrete existing header and body rejection tags the original safe validation error.</summary>
    [Test]
    public void EveryGuardRejectionHasDefinedStageAndUnchangedValidation()
    {
        foreach (var fault in Enum.GetValues<McpTransportHeaderFault>())
        {
            AssertRejected(McpTransportGuardTestData.InvalidHeaders(fault), null, ExpectedStage(fault));
        }

        foreach (var fault in Enum.GetValues<McpTransportFrameFault>())
        {
            AssertRejected(McpTransportGuardTestData.Headers(), McpTransportGuardTestData.InvalidFrame(fault), ExpectedStage(fault));
        }
    }

    /// <summary>Actual rejected headers log only closed values through Microsoft's EventSource provider.</summary>
    [Test]
    public async Task ActualGuardFailureLogsClosedValuesWithoutHeaderOrMethodCanaries()
    {
        var headers = McpTransportGuardTestData.Headers(MethodCanary + "\n" + HeaderCanary, HeaderCanary);
        headers[PrivateHeader] = HeaderCanary;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.ReadHeaders(headers, UnitMcpOptions.Execution()));
        using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
        using var capture = new McpTransportEventSourceCapture();
        var logger = factory.CreateLogger(nameof(McpTransportDiagnosticsTests));
        await Assert.That(logger.IsEnabled(LogLevel.Warning)).IsTrue();
        McpTransportDiagnostics.Log(logger, error, headers, UnitMcpOptions.Execution());

        await Assert.That(McpTransportDiagnostics.HasStage(error)).IsTrue();
        await Assert.That(capture.Text).Contains(nameof(McpTransportStage.MethodHeaderEncoding));
        await Assert.That(capture.Text).Contains(CategoryOther);
        await Assert.That(capture.Text).DoesNotContain(MethodCanary);
        await Assert.That(capture.Text).DoesNotContain(HeaderCanary);
        await Assert.That(capture.Text).DoesNotContain(error.GetType().Name);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpTransportProtocol.InvalidTransport);
    }

    /// <summary>Unknown metadata and arbitrary method strings become closed defaults before logging.</summary>
    [Test]
    public async Task UnknownMetadataAndMethodCannotEnterEventSourceOutput()
    {
        var headers = McpTransportGuardTestData.Headers(MethodCanary, HeaderCanary);
        var error = Errors.Fail(ErrorCode.Validation, MetadataCanary);
        error.Data[McpTransportDiagnostics.StageMetadataKey] = (McpTransportStage)int.MaxValue;
        using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
        using var capture = new McpTransportEventSourceCapture();
        var logger = factory.CreateLogger(nameof(McpTransportDiagnosticsTests));
        await Assert.That(logger.IsEnabled(LogLevel.Warning)).IsTrue();
        McpTransportDiagnostics.Log(logger, error, headers, UnitMcpOptions.Execution());

        await Assert.That(McpTransportDiagnostics.HasStage(error)).IsFalse();
        await Assert.That(capture.Text).Contains(nameof(McpTransportStage.Unknown));
        await Assert.That(capture.Text).Contains(CategoryOther);
        await Assert.That(capture.Text).DoesNotContain(MetadataCanary);
        await Assert.That(capture.Text).DoesNotContain(MethodCanary);
        await Assert.That(capture.Text).DoesNotContain(HeaderCanary);
    }

    /// <summary>A routed body with no target field records the missing-target stage.</summary>
    [Test]
    public async Task MissingTargetHasItsOwnStage()
    {
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(), UnitMcpOptions.Execution());
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.Inspect(
            McpTransportGuardTestData.Frame(RequestMethods.ToolsCall, new()), headers));

        await Assert.That(error.Data[McpTransportDiagnostics.StageMetadataKey]).IsEqualTo(McpTransportStage.MissingTarget);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpTransportProtocol.InvalidTransport);
    }

    /// <summary>Known protocol method categories are emitted by name without retaining the caller string.</summary>
    [Test]
    public async Task KnownMethodCategoryIsClosed()
    {
        var error = Errors.Fail(ErrorCode.Validation, McpTransportProtocol.InvalidTransport);
        error.Data[McpTransportDiagnostics.StageMetadataKey] = McpTransportStage.BodyMethodMismatch;
        using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
        using var capture = new McpTransportEventSourceCapture();
        var logger = factory.CreateLogger(nameof(McpTransportDiagnosticsTests));
        await Assert.That(logger.IsEnabled(LogLevel.Warning)).IsTrue();
        foreach (var method in new[]
        {
            "server/discover", "initialize", RequestMethods.ToolsCall, "tools/list", MethodCanary
        })
        {
            McpTransportDiagnostics.Log(logger, error, McpTransportGuardTestData.Headers(method, null), UnitMcpOptions.Execution());
        }

        await Assert.That(capture.Text).Contains(CategoryToolsCall);
        await Assert.That(capture.Text).Contains("Discovery");
        await Assert.That(capture.Text).Contains("Initialize");
        await Assert.That(capture.Text).Contains("ToolsList");
        await Assert.That(capture.Text).Contains(CategoryOther);
        await Assert.That(capture.Text).DoesNotContain(MethodCanary);
        await Assert.That(capture.Text).DoesNotContain(nameof(KeyLoadException));
    }

    private static McpTransportStage ExpectedStage(McpTransportHeaderFault fault) => fault switch
    {
        McpTransportHeaderFault.MissingRevision or McpTransportHeaderFault.EmptyRevision
            or McpTransportHeaderFault.DuplicateRevision => McpTransportStage.ProtocolRevisionCount,
        McpTransportHeaderFault.LegacyRevision or McpTransportHeaderFault.RevisionWhitespace
            or McpTransportHeaderFault.MarkerRevision => McpTransportStage.ProtocolRevisionValue,
        McpTransportHeaderFault.Session or McpTransportHeaderFault.EmptySession
            => McpTransportStage.SessionHeaderPresence,
        McpTransportHeaderFault.LastEvent or McpTransportHeaderFault.EmptyLastEvent
            => McpTransportStage.LastEventHeaderPresence,
        McpTransportHeaderFault.DuplicateMethod or McpTransportHeaderFault.EmptyMethod
            or McpTransportHeaderFault.EmptyMethodValues => McpTransportStage.MethodHeaderShape,
        McpTransportHeaderFault.DuplicateName or McpTransportHeaderFault.EmptyName
            or McpTransportHeaderFault.EmptyNameValues => McpTransportStage.NameHeaderShape,
        McpTransportHeaderFault.NonAsciiMethod or McpTransportHeaderFault.ControlMethod
            or McpTransportHeaderFault.DeleteMethod => McpTransportStage.MethodHeaderEncoding,
        McpTransportHeaderFault.NonAsciiName or McpTransportHeaderFault.ControlName
            or McpTransportHeaderFault.DeleteName => McpTransportStage.NameHeaderEncoding,
        _ => throw new ArgumentOutOfRangeException(nameof(fault))
    };

    private static McpTransportStage ExpectedStage(McpTransportFrameFault fault) => fault switch
    {
        McpTransportFrameFault.MethodMismatch => McpTransportStage.BodyMethodMismatch,
        McpTransportFrameFault.MethodWrongType => McpTransportStage.BodyMethodShape,
        McpTransportFrameFault.NameMismatch or McpTransportFrameFault.NameWrongType => McpTransportStage.TargetMismatch,
        McpTransportFrameFault.VersionMismatch or McpTransportFrameFault.VersionWrongType
            or McpTransportFrameFault.VersionNull => McpTransportStage.ParameterRevision,
        McpTransportFrameFault.MetaMismatch or McpTransportFrameFault.MetaWrongType
            or McpTransportFrameFault.MetaNull => McpTransportStage.MetadataRevision,
        _ => throw new ArgumentOutOfRangeException(nameof(fault))
    };

    private static void AssertRejected(IHeaderDictionary headers, byte[]? body, McpTransportStage expectedStage)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            if (body is null)
            {
                _ = McpTransportGuard.ReadHeaders(headers, UnitMcpOptions.Execution());
            }
            else
            {
                McpTransportGuard.Inspect(body, McpTransportGuard.ReadHeaders(headers, UnitMcpOptions.Execution()));
            }
        });
        if (!McpTransportDiagnostics.HasStage(error)
            || error.Data[McpTransportDiagnostics.StageMetadataKey] is not McpTransportStage actualStage
            || actualStage != expectedStage)
        {
            throw new InvalidOperationException("The rejection did not carry its expected defined diagnostic stage.");
        }

        if (error.Code != ErrorCode.Validation || error.Message != McpTransportProtocol.InvalidTransport)
        {
            throw new InvalidOperationException("The original public validation reply changed.");
        }
    }
}

/// <summary>AC-CLIENT-008: native captures remain enabled across real provider/listener disposal and recreation.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class McpTransportCaptureLifecycleTests
{
    private const int CaptureLifetimes = 2;
    private const string MethodCanary = "private-lifecycle-method-canary";

    /// <summary>Two independently owned native capture lifetimes preserve closed categories and privacy.</summary>
    [Test]
    public async Task NativeProviderCaptureCanBeDisposedAndReenabled()
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
