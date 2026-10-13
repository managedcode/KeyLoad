using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel(LoggingEventSourceIsolation.Key)]
internal sealed class PartitionMoveOwnerDiagnosticsTests
{
    private const string PrivateCanary = "private-movement-owner-payload-canary";
    private const string OwnerDetail = "The partition movement owner no longer matches its admission.";
    private const string UnavailableDetail = "The database request could not complete. Retry the same command ID for writes.";

    /// <summary>Closed owner markers preserve public failures and exclude private exception metadata from native logs.</summary>
    [Test]
    public async Task ClosedOwnerMarkersAndMalformedMetadataPreserveRepliesWithoutLoggingPrivateText()
    {
        var guards = Enum.GetValues<PartitionMoveOwnerGuard>();
        await Assert.That(guards.Length).IsEqualTo(21);
        foreach (var guard in guards.Where(value => value != PartitionMoveOwnerGuard.Unobserved))
        {
            var error = PartitionMoveOwnerDiagnostic.Fail(guard);
            await Assert.That(error.Data.Count).IsEqualTo(1);
            await Assert.That(error.Data[PartitionMoveOwnerDiagnostic.MetadataKey] is int value && value == (int)guard).IsTrue();
            await RequireAsync(error, guard, ErrorCode.OwnershipLost, OwnerDetail);
        }
        foreach (var error in InvalidInputs())
        {
            var code = error is KeyLoadException domain ? domain.Code : ErrorCode.OwnershipLost;
            var detail = error is KeyLoadException typed ? typed.Message : UnavailableDetail;
            await RequireAsync(error, PartitionMoveOwnerGuard.Unobserved, code, detail);
        }
    }

    private static IEnumerable<Exception> InvalidInputs()
    {
        var text = PartitionMoveOwnerDiagnostic.Fail(PartitionMoveOwnerGuard.DispatchIdentity);
        text.Data[PartitionMoveOwnerDiagnostic.MetadataKey] = PrivateCanary;
        yield return text;
        var number = PartitionMoveOwnerDiagnostic.Fail(PartitionMoveOwnerGuard.DispatchIdentity);
        number.Data[PartitionMoveOwnerDiagnostic.MetadataKey] = int.MaxValue;
        yield return number;
        var wrongCode = Errors.Fail(ErrorCode.PermissionDenied, OwnerDetail);
        wrongCode.Data[PartitionMoveOwnerDiagnostic.MetadataKey] = (int)PartitionMoveOwnerGuard.DispatchIdentity;
        yield return wrongCode;
        var wrongDetail = Errors.Fail(ErrorCode.OwnershipLost, PrivateCanary);
        wrongDetail.Data[PartitionMoveOwnerDiagnostic.MetadataKey] = (int)PartitionMoveOwnerGuard.DispatchIdentity;
        yield return wrongDetail;
        var wrongType = new InvalidOperationException(PrivateCanary);
        wrongType.Data[PartitionMoveOwnerDiagnostic.MetadataKey] = (int)PartitionMoveOwnerGuard.DispatchIdentity;
        yield return wrongType;
        yield return PartitionMoveOwnerDiagnostic.Fail(PartitionMoveOwnerGuard.Unobserved);
        yield return PartitionMoveOwnerDiagnostic.Fail((PartitionMoveOwnerGuard)int.MaxValue);
    }

    private static async Task RequireAsync(Exception error, PartitionMoveOwnerGuard guard, ErrorCode code, string detail)
    {
        var requestId = Guid.NewGuid();
        using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
        using var capture = new EventSourceLogCapture();
        var reply = GrainReplyFactory.Failure(error, false, factory.CreateLogger(nameof(GrainFailureDiagnosticsTests)),
            UnitRoutingOptions.Routing(), requestId, GrainFailureStage.CapabilityExecution);
        await Assert.That(PartitionMoveOwnerDiagnostic.Read(error)).IsEqualTo(guard);
        await Assert.That(reply.Error).IsEqualTo(code);
        await Assert.That(reply.SafeDetail).IsEqualTo(detail);
        await Assert.That(reply.Payload.IsEmpty).IsTrue();
        await Assert.That(capture.Text).Contains(requestId.ToString());
        await Assert.That(capture.Text).Contains(nameof(GrainFailureStage.CapabilityExecution));
        await Assert.That(capture.Text).Contains(code.ToString());
        await Assert.That(capture.Text).Contains(error is KeyLoadException ? nameof(GrainFailureCategory.Domain) : nameof(GrainFailureCategory.Unexpected));
        await Assert.That(capture.Text).Contains(guard.ToString());
        await Assert.That(capture.Text).DoesNotContain(PrivateCanary);
        await Assert.That(capture.Text).DoesNotContain(PartitionMoveOwnerDiagnostic.MetadataKey);
        await Assert.That(capture.Text).DoesNotContain(nameof(InvalidOperationException));
        await Assert.That(capture.Text).DoesNotContain(nameof(KeyLoadException));
        if (error is KeyLoadException domain)
        {
            await RequireProblemAsync(domain, code, detail);
        }
    }

    private static async Task RequireProblemAsync(KeyLoadException error, ErrorCode code, string detail)
    {
        var problem = error.ToProblem();
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(error.Message).IsEqualTo(detail);
        await Assert.That(error.StatusCode).IsEqualTo(code == ErrorCode.OwnershipLost ? 503 : 403);
        await Assert.That(problem.Type).IsEqualTo("urn:keyload:error:" + code);
        await Assert.That(problem.Title).IsEqualTo(code.ToString());
        await Assert.That(problem.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(problem.Detail).IsEqualTo(detail);
        await Assert.That(problem.StatusCode).IsEqualTo(error.StatusCode);
    }
}
