using System.Globalization;
using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.Constants;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsProtocolCases
{
    private const string SafeDetail = "The database request failed safely.";
    private const string ExtraExtension = "privateDiagnostic";
    private const string UnknownErrorName = "FuturePrivateError";
    private const string ProblemTypePrefix = "urn:keyload:error:";
    private const int ExtraReplyByte = 1;

    internal static async Task AcCrs004ClosedProblemMapsEveryDefinedErrorCodeExactly()
    {
        foreach (var code in Enum.GetValues<ErrorCode>())
        {
            var problem = GrainRequestStreamProblem.Create(code, SafeDetail);
            await Assert.That(GrainRequestStreamProblem.ReadCode(problem)).IsEqualTo(code);
            await Assert.That(problem.Type).IsEqualTo(ProblemTypePrefix + code);
            await Assert.That(problem.Title).IsEqualTo(code.ToString());
            await Assert.That(problem.StatusCode).IsEqualTo(Errors.Status(code));
            await Assert.That(problem.Detail).IsEqualTo(SafeDetail);
            await Assert.That(problem.Instance).IsNull();
            await Assert.That(problem.Extensions.Count).IsEqualTo(1);
            await Assert.That(problem.Extensions[ProblemConstants.ExtensionKeys.ErrorCode]).IsEqualTo(code.ToString());
        }
    }

    internal static async Task AcCrs004OpenProblemShapesFailClosed(RequestCqrsProblemMutation mutation)
    {
        var problem = CreateProblem(mutation);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => GrainRequestStreamProblem.Validate(problem));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }

    internal static async Task AcCrs004ActualClientSerializerCounterIsInclusiveAndCancellationAware(RequestCqrsClusterFixture fixture)
    {
        var serializer = fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<string>>();
        var sample = new string('n', 8_192);
        var native = serializer.SerializeToArray(sample);
        await Assert.That(GrainNativeByteCounter.Measure(serializer, sample, native.Length, CancellationToken.None))
            .IsEqualTo((long)native.Length);
        var oneUnder = Assert.ThrowsExactly<KeyLoadException>(() =>
            GrainNativeByteCounter.Measure(serializer, sample, native.Length - ExtraReplyByte, CancellationToken.None));
        await Assert.That(oneUnder.Code).IsEqualTo(ErrorCode.BudgetExceeded);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() =>
            GrainNativeByteCounter.Measure(serializer, sample, native.Length, cancelled.Token)).CancellationToken)
            .IsEqualTo(cancelled.Token);
    }

    internal static async Task AcCrs004ScratchAdmissionHonorsActualAdvanceNotReservationHint()
    {
        using var writer = new GrainNativeCountingWriter(20, CancellationToken.None);
        _ = writer.GetSpan(8);
        writer.Advance(8);
        var largerThanRemaining = writer.GetMemory(16);
        await Assert.That(largerThanRemaining.Length).IsGreaterThanOrEqualTo(16);
        writer.Advance(12);
        await Assert.That(writer.Length).IsEqualTo(20L);
        var excess = Assert.ThrowsExactly<KeyLoadException>(() => writer.Advance(1));
        await Assert.That(excess.Code).IsEqualTo(ErrorCode.BudgetExceeded);

        using var bounded = new GrainNativeCountingWriter(32, CancellationToken.None);
        var overScratch = Assert.ThrowsExactly<KeyLoadException>(() =>
            bounded.GetSpan(GrainRequestStreamProtocol.MaximumScratchBytes + ExtraReplyByte));
        await Assert.That(overScratch.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    internal static async Task AcCrs004NativeSixteenMiBReplyUsesActualRegisteredChunkSerializer(RequestCqrsClusterFixture fixture)
    {
        var serializer = fixture.Cluster.ServiceProvider.GetRequiredService<
            Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        var payload = new byte[GrainRoutingProtocol.MaximumReplyBytes];
        var chunk = CompletedChunk(payload);
        var native = serializer.SerializeToArray(chunk);
        var started = StartedChunk(Guid.NewGuid());
        var startedBytes = serializer.SerializeToArray(started).Length;
        await Assert.That(native.Length).IsLessThanOrEqualTo(GrainRequestStreamProtocol.MaximumCompletedBytes);
        await Assert.That((long)startedBytes + native.Length)
            .IsLessThanOrEqualTo(GrainRequestStreamProtocol.MaximumAggregateBytes);
        await Assert.That(GrainNativeByteCounter.Measure(serializer, chunk, native.Length, CancellationToken.None))
            .IsEqualTo((long)native.Length);
        var oneUnder = Assert.ThrowsExactly<KeyLoadException>(() =>
            GrainNativeByteCounter.Measure(serializer, chunk, native.Length - ExtraReplyByte, CancellationToken.None));
        await Assert.That(oneUnder.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static Problem CreateProblem(RequestCqrsProblemMutation mutation)
    {
        var problem = GrainRequestStreamProblem.Create(ErrorCode.PermissionDenied, SafeDetail);
        switch (mutation)
        {
            case RequestCqrsProblemMutation.ExtraExtension:
                problem.Extensions.Add(ExtraExtension, "do-not-disclose");
                break;
            case RequestCqrsProblemMutation.NumericErrorCode:
                problem.Extensions[ProblemConstants.ExtensionKeys.ErrorCode] = (int)ErrorCode.PermissionDenied;
                break;
            case RequestCqrsProblemMutation.UnknownErrorCode:
                problem.Extensions[ProblemConstants.ExtensionKeys.ErrorCode] = UnknownErrorName;
                break;
            case RequestCqrsProblemMutation.NumericEnumName:
                problem.Extensions[ProblemConstants.ExtensionKeys.ErrorCode] = int.MaxValue.ToString(CultureInfo.InvariantCulture);
                break;
            case RequestCqrsProblemMutation.WrongTitle:
                problem.Title = "InternalException";
                break;
            case RequestCqrsProblemMutation.WrongType:
                problem.Type = "about:blank";
                break;
            case RequestCqrsProblemMutation.WrongStatus:
                problem.StatusCode++;
                break;
            case RequestCqrsProblemMutation.Instance:
                problem.Instance = "/private/partition/key";
                break;
            case RequestCqrsProblemMutation.ExcessDetail:
                problem.Detail = new string('x', GrainRequestStreamProtocol.MaximumDetailCharacters + ExtraReplyByte);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        return problem;
    }

    internal static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> StartedChunk(Guid requestId,
        long sequence = 1, string? eventType = null)
        => new(CqrsStreamChunkKind.Started,
            Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(requestId)), null, null,
            eventType, null, sequence);

    internal static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> CompletedChunk(ReadOnlyMemory<byte> payload,
        long sequence = 2)
        => new(CqrsStreamChunkKind.Completed, null,
            Result<GrainOperationReply>.Succeed(new GrainOperationReply { Payload = payload }), null,
            null, null, sequence);
}

internal enum RequestCqrsProblemMutation
{
    ExtraExtension,
    NumericErrorCode,
    UnknownErrorCode,
    NumericEnumName,
    WrongTitle,
    WrongType,
    WrongStatus,
    Instance,
    ExcessDetail
}
