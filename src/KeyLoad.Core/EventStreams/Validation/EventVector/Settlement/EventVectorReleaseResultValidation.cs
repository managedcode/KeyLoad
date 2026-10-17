using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorReleaseResultValidation
{
    private const int Version = 1;
    private const long NextGeneration = 1;
    private const long EmptyPosition = 0;
    private const string InvalidPass = "The complete event vector release pass is inconsistent.";

    internal static void Require(EventVectorSourcePhase phase, EventVectorSourceMutation request,
        EventVectorSourceReleaseResult result, EventVectorAdmissionPolicy admission)
    {
        if (result.Version != Version || result.CommandId != phase.PhaseCommandId || result.Source != phase.Source
            || result.OriginalPinCommandId != request.OriginalPinCommandId || !Enum.IsDefined(result.Disposition)
            || result.Token is null || result.Token.Incarnation != phase.SourceOwner.Incarnation
            || result.Token.AtomicPartitionId != phase.Source.Partition.AtomicPartitionId
            || result.Token.OwnershipEpoch != phase.SourceOwner.PlacementEpoch || result.Token.Position <= EmptyPosition
            || !Enum.IsDefined(result.Durability)
            || !result.OriginalOutcomeDigest.IsEmpty && result.OriginalOutcomeDigest.Length != SHA256.HashSizeInBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidPass);
        }
        if (result.Disposition == EventVectorReleaseDisposition.Released)
        {
            if (result.RetainedPin is not null)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidPass);
            }
            return;
        }
        var pin = result.RetainedPin ?? throw Errors.Fail(ErrorCode.Corruption, InvalidPass);
        var original = EventVectorOriginalPinBinding.Read(request, admission);
        EventVectorOriginalPinBinding.RequireRetained(request, original, pin);
        if (!pin.Released || pin.CoverageGeneration != request.ExpectedCoverageGeneration
            || pin.CoverageGeneration != request.CoverageGeneration || pin.Position != request.Position
            || pin.PinRevision != checked(request.ExpectedPinRevision + NextGeneration)
            || !result.OriginalOutcomeDigest.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidPass);
        }
    }
}
