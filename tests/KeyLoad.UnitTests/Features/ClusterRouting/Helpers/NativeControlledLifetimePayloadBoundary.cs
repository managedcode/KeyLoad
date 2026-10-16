using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Tests genuine complete signed native issuer ceilings separately from the controlled transport carrier.</summary>
internal static class NativeControlledLifetimePayloadBoundary
{
    internal const int MaximumBytes = 8_192;
    private const int BoundaryStep = 1;
    private const int EmptyCharacters = 0;
    private const int SizingAttempts = 16;
    private const char ValueCharacter = 'x';
    private const string JsonPrefix = "{\"payload\":\"";
    private const string JsonSuffix = "\"}";
    private const string PrincipalId = "root";
    private const string WitnessKind = "NativeControlledLifetimeIssuerBudget";
    private static readonly Guid CommandId = new("53e48378-a833-46ec-a659-0b2f9e9b606d");

    internal static async Task AssertAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, CancellationToken token)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        var receiver = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        var exact = FindLargest(source, token);
        var belowRequest = Request(source, exact.Characters - BoundaryStep);
        var below = source.Database.CreateNativeOperation(OperationKind.Batch, CommandId, PrincipalId,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(belowRequest));
        await Assert.That(below.NativePayload.Length).IsLessThanOrEqualTo(MaximumBytes);
        await Assert.That(exact.Operation.NativePayload.Length).IsLessThanOrEqualTo(MaximumBytes);
        var original = NativeCommandPayload.Read<CommandRequest>(exact.Operation);
        await ResourceLifetimeGrantAssertions.EqualAsync(exact.Request, original);
        var greater = Request(source, exact.Characters + BoundaryStep);
        var bytes = NativeSerialization.Serialize(greater);
        await Assert.That(bytes.Length).IsEqualTo(NativeSerialization.Serialize(exact.Request).Length + BoundaryStep);
        var refusal = Assert.ThrowsExactly<KeyLoadException>(() => source.Database.CreateNativeOperation(
            OperationKind.Batch, CommandId, PrincipalId, source.Database.EvaluationClock.GetUtcNow(), bytes));
        await Assert.That(refusal.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await before.AssertUnchangedAsync(source);
        await receiver.AssertUnchangedAsync(target);
        await Console.Out.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            Kind = WitnessKind,
            MaximumBytes,
            LargestSignedBytes = exact.Operation.NativePayload.Length,
            LargestTypedBytes = NativeSerialization.Serialize(exact.Request).Length,
            SmallerSignedBytes = below.NativePayload.Length,
            OneCharacterLargerTypedBytes = bytes.Length,
            Refusal = refusal.Code
        }));
    }

    private static (int Characters, CommandRequest Request, ReplicatedOperation Operation) FindLargest(
        ControlledPartitionMovementNode source, CancellationToken token)
    {
        var low = EmptyCharacters;
        var high = MaximumBytes;
        (int Characters, CommandRequest Request, ReplicatedOperation Operation)? largest = null;
        for (var attempt = EmptyCharacters; attempt < SizingAttempts && low <= high; attempt += BoundaryStep)
        {
            token.ThrowIfCancellationRequested();
            var size = low + (high - low) / (BoundaryStep + BoundaryStep);
            var request = Request(source, size);
            ReplicatedOperation? actual = null;
            try
            {
                actual = source.Database.CreateNativeOperation(OperationKind.Batch, CommandId, PrincipalId,
                    source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.ResourceExhausted) { }
            if (actual is not null)
            {
                largest = (size, request, actual);
                low = size + BoundaryStep;
            }
            else
            {
                high = size - BoundaryStep;
            }
        }
        if (low <= high || largest is not { Characters: > EmptyCharacters } admitted)
        {
            throw new InvalidOperationException("The genuine bounded native issuer search did not settle.");
        }
        return admitted;
    }

    private static CommandRequest Request(ControlledPartitionMovementNode source, int characters)
    {
        var placement = source.Database.ReadAtomicPartitionPlacement(PrincipalId,
            new(AtomicPartitionPlacementProtocol.CurrentVersion, ControlledPartitionMovementCorpus.Partition));
        return new(CommandId, ControlledPartitionMovementCorpus.Partition,
            [new PutDocument(ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.DocumentId,
                JsonPrefix + new string(ValueCharacter, characters) + JsonSuffix,
                ResourceLifetimeGrantStages.InitialRevision, ExplicitReplacement: true)], placement.PlacementEpoch);
    }
}
