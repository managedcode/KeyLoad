using System.Globalization;
using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.Constants;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsProtocolCases
{
    private const string SafeDetail = "The database request failed safely.";
    private const string ExtraExtension = "privateDiagnostic";
    private const string UnknownErrorName = "FuturePrivateError";
    private const string ProblemTypePrefix = "urn:keyload:error:";

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

    internal static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> CompletedChunk(object payload,
        long sequence = 2)
        => new(CqrsStreamChunkKind.Completed, null,
            Result<GrainOperationReply>.Succeed(new GrainOperationReply { Result = new GrainValue(payload) }), null,
            null, null, sequence);
}
