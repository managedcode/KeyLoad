using System.Runtime.Serialization;
using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.UnitTests.TestInfrastructure;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.OrleansExecution;

/// <summary>Exercises the real reply mapper and execution logging boundary without exposing exception content.</summary>
[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class GrainFailureCauseDiagnosticsTests
{
    private const string MessageCanary = "private-cause-constructor-canary";
    private const string DataCanary = "private-cause-data-key-canary";
    private const string Unavailable = "The database request could not complete. Retry the same command ID for writes.";
    private const string Interrupted = "The write outcome is unknown. Retry the same command ID.";
    private const string SafeDenied = "The selected operation is denied.";
    private const string CausePrefix = "Closed cause ";
    private const string CauseSuffix = ".";
    private const string HealthyId = "closed-cause-healthy";
    private const long HealthyPosition = 1;
    private const int FirstDepth = 0;
    private const int MaximumDepth = 4;
    private const int BeyondDepth = 5;
    private const int MissingReferenceArrayLength = 1;
    private const int FirstMissingReference = 0;

    [Test]
    public async Task UnexpectedFamiliesPreservePublicRepliesPrivateReferencesAndFollowingSuccess()
    {
        foreach (var (error, kind) in UnexpectedCases())
        {
            await RequireAsync(error, kind, GrainFailureCategory.Unexpected,
                ErrorCode.OwnershipLost, ErrorCode.UnknownWriteOutcome, Unavailable, Unavailable);
        }
        await HealthyAsync();
    }

    [Test]
    public async Task AggregatePrimaryIsBoundedWithoutInspectingSecondaryOrMutatingOriginals()
    {
        var primary = new InvalidOperationException(MessageCanary);
        var secondary = new IOException(MessageCanary);
        var aggregate = new AggregateException(MessageCanary, primary, secondary);
        await RequireAsync(aggregate, GrainFailureCauseKind.State, GrainFailureCategory.Unexpected,
            ErrorCode.OwnershipLost, ErrorCode.UnknownWriteOutcome, Unavailable, Unavailable);
        await Assert.That(aggregate.InnerExceptions.First()).IsSameReferenceAs(primary);
        await Assert.That(aggregate.InnerExceptions.Last()).IsSameReferenceAs(secondary);
        await RequireAsync(Wrap(primary, MaximumDepth), GrainFailureCauseKind.State, GrainFailureCategory.Unexpected,
            ErrorCode.OwnershipLost, ErrorCode.UnknownWriteOutcome, Unavailable, Unavailable);
        await RequireAsync(Wrap(primary, BeyondDepth), GrainFailureCauseKind.AggregateDepthLimit, GrainFailureCategory.Unexpected,
            ErrorCode.OwnershipLost, ErrorCode.UnknownWriteOutcome, Unavailable, Unavailable);
        await RequireAsync(new AggregateException(), GrainFailureCauseKind.Other, GrainFailureCategory.Unexpected,
            ErrorCode.OwnershipLost, ErrorCode.UnknownWriteOutcome, Unavailable, Unavailable);
        await HealthyAsync();
    }

    [Test]
    public async Task OrdinaryCategoriesRemainUnobservedWithTheirOriginalPublicCodesAndDetails()
    {
        await RequireAsync(new JsonException(MessageCanary), GrainFailureCauseKind.Unobserved, GrainFailureCategory.Json,
            ErrorCode.Validation, ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest, GrainRoutingProtocol.InvalidRequest);
        await RequireAsync(new ArgumentException(MessageCanary), GrainFailureCauseKind.Unobserved, GrainFailureCategory.Argument,
            ErrorCode.Validation, ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest, GrainRoutingProtocol.InvalidRequest);
        await RequireAsync(Errors.Fail(ErrorCode.PermissionDenied, SafeDenied), GrainFailureCauseKind.Unobserved,
            GrainFailureCategory.Domain, ErrorCode.PermissionDenied, ErrorCode.PermissionDenied, SafeDenied, SafeDenied);
        await RequireAsync(new OperationCanceledException(MessageCanary), GrainFailureCauseKind.Unobserved,
            GrainFailureCategory.Cancellation, ErrorCode.OwnershipLost, ErrorCode.UnknownWriteOutcome, Unavailable, Interrupted);
        await HealthyAsync();
    }

    private static IEnumerable<(Exception Error, GrainFailureCauseKind Kind)> UnexpectedCases()
    {
        yield return (new InvalidOperationException(MessageCanary), GrainFailureCauseKind.State);
        yield return (new ObjectDisposedException(MessageCanary), GrainFailureCauseKind.ReleasedState);
        yield return (CaptureRuntimeReference(), GrainFailureCauseKind.Reference);
        yield return (new InvalidCastException(MessageCanary), GrainFailureCauseKind.Conversion);
        yield return (new FormatException(MessageCanary), GrainFailureCauseKind.Conversion);
        yield return (new OverflowException(MessageCanary), GrainFailureCauseKind.Conversion);
        yield return (new TypeInitializationException(MessageCanary, new IOException(MessageCanary)), GrainFailureCauseKind.Initialization);
        yield return (new TypeLoadException(MessageCanary), GrainFailureCauseKind.TypeResolution);
        yield return (new MissingMemberException(MessageCanary), GrainFailureCauseKind.TypeResolution);
        yield return (new NotSupportedException(MessageCanary), GrainFailureCauseKind.Unsupported);
        yield return (new SerializationException(MessageCanary), GrainFailureCauseKind.Serialization);
        yield return (new IOException(MessageCanary), GrainFailureCauseKind.StorageIo);
        yield return (new TimeoutException(MessageCanary), GrainFailureCauseKind.Other);
    }

    private static NullReferenceException CaptureRuntimeReference()
    {
        try
        {
            var missing = new string[MissingReferenceArrayLength];
            _ = missing[FirstMissingReference].Length;
        }
        catch (NullReferenceException original)
        {
            return original;
        }
        throw new InvalidOperationException(MessageCanary);
    }

    private static Exception Wrap(Exception primary, int count)
    {
        for (var depth = FirstDepth; depth < count; depth++)
        {
            primary = new AggregateException(MessageCanary, primary);
        }
        return primary;
    }

    private static async Task RequireAsync(Exception error, GrainFailureCauseKind kind, GrainFailureCategory category,
        ErrorCode readCode, ErrorCode commandCode, string readDetail, string commandDetail)
    {
        var privateValue = new object();
        error.Data[DataCanary] = privateValue;
        var originalData = error.Data;
        var originalInner = error.InnerException;
        var originalCount = error.Data.Count;
        foreach (var command in new[] { false, true })
        {
            var requestId = Guid.NewGuid();
            using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
            using var capture = new EventSourceLogCapture();
            var reply = GrainReplyFactory.Failure(error, command, factory.CreateLogger(nameof(GrainFailureDiagnosticsTests)),
                UnitRoutingOptions.Routing(), requestId, GrainFailureStage.CapabilityExecution);
            var output = capture.Text;
            await Assert.That(reply.Error).IsEqualTo(command ? commandCode : readCode);
            await Assert.That(reply.SafeDetail).IsEqualTo(command ? commandDetail : readDetail);
            await Assert.That(reply.Payload.IsEmpty).IsTrue();
            await Assert.That(output).Contains(requestId.ToString());
            await Assert.That(output).Contains(nameof(GrainFailureStage.CapabilityExecution));
            await Assert.That(output).Contains(category.ToString());
            await Assert.That(output).Contains(CausePrefix + kind + CauseSuffix);
            await Assert.That(output).DoesNotContain(MessageCanary);
            await Assert.That(output).DoesNotContain(DataCanary);
            await Assert.That(output).DoesNotContain(error.GetType().Name);
            if (originalInner is not null)
            {
                await Assert.That(output).DoesNotContain(originalInner.GetType().Name);
            }
            await Assert.That(error.Data).IsSameReferenceAs(originalData);
            await Assert.That(error.Data[DataCanary]).IsSameReferenceAs(privateValue);
            await Assert.That(error.Data.Count).IsEqualTo(originalCount);
            await Assert.That(error.InnerException).IsSameReferenceAs(originalInner);
        }
    }

    private static async Task HealthyAsync()
    {
        var expected = new BackupReceipt(HealthyId, HealthyPosition);
        var options = UnitRoutingOptions.Routing();
        var reply = GrainReplyFactory.Value(expected, options, CancellationToken.None);
        var stream = GrainReplyFactory.StreamResult(reply, options);
        await Assert.That(stream.IsSuccess).IsTrue();
        await Assert.That(stream.Value).IsSameReferenceAs(reply);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value).IsEqualTo(expected);
    }
}
