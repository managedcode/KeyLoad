using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Retains closed original SDK failure facts without a second operation or response-body read.</summary>
internal static class PartitionMovementPublicParentRf3SdkFailure
{
    private const int SchemaVersion = 1;
    private const int MaximumCharacters = 1024;
    private const string WitnessKind = "PublicMovementOriginalSdkFailure";
    private const string FirstMove = "FirstPublicMove";
    private const string InvalidWitness = "The original SDK failure witness exceeds its bound.";
    private const string DeadlineExceeded = "The read execution deadline is exceeded.";
    private const string ResultBytesExceeded = "The read result byte budget is exceeded.";
    private const string TextTokensExceeded = "The search corpus token budget is exceeded.";

    internal static async Task<PartitionMoveResult> RequireSuccessAsync(Result<PartitionMoveResult> actual,
        Guid originalMoveId)
    {
        try
        {
            return await McpCallerAssertions.SdkSuccessAsync(actual);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            ServerFailureObserver.Observe(() => Write(actual.Problem, originalMoveId), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static void Write(Problem? actual, Guid originalMoveId)
    {
        var code = Enum.TryParse<ErrorCode>(actual?.ErrorCode, out var parsed) && Enum.IsDefined(parsed)
            && parsed.ToString() == actual?.ErrorCode ? parsed.ToString() : null;
        var expected = code is null ? null : Errors.Problem(parsed, string.Empty);
        var detail = ClosedDetail(actual?.Detail);
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            kind = WitnessKind,
            phase = FirstMove,
            moveId = originalMoveId,
            problemPresent = actual is not null,
            errorCode = code,
            status = actual?.StatusCode,
            type = actual?.Type == expected?.Type ? actual?.Type : null,
            title = actual?.Title == expected?.Title ? actual?.Title : null,
            typeMatchesCanonical = expected is not null && actual?.Type == expected.Type,
            titleMatchesCanonical = expected is not null && actual?.Title == expected.Title,
            detail,
            detailRecognized = detail is not null,
            detailCharacters = actual?.Detail?.Length,
            instancePresent = actual?.Instance is not null,
            extensionCount = actual?.Extensions.Count
        });
        if (json.Length > MaximumCharacters)
        {
            throw new InvalidOperationException(InvalidWitness);
        }
        Console.Error.WriteLine(json);
    }

    private static string? ClosedDetail(string? actual)
        => actual is DeadlineExceeded or ResultBytesExceeded or TextTokensExceeded
            or ReadExecutionBudget.ReadBytesExceeded or GrainRoutingProtocol.ReplyBudgetExceeded
            or PartitionMoveProtocol.Capacity or PartitionMovementFinalOutcomeCapacityProtocol.Exceeded
            or ResourceOwnerInventoryProtocol.Capacity ? actual : null;
}
