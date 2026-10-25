using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using static KeyLoad.FunctionalTests.Search.OnlineTextProgressFrames;
using Chunk = ManagedCode.Communication.CQRS.CqrsStreamChunk<KeyLoad.Clustering.GrainRequestProgress, KeyLoad.Clustering.GrainOperationReply>;

namespace KeyLoad.FunctionalTests.Search;

internal static class OnlineTextProgressShapeRefusals
{
    private const string WrongMessage = "Unexpected online progress message.";
    private const string WrongEvent = "unexpected-online-progress";
    private const string WrongEventId = "unexpected-online-event-id";
    private const int InvalidPhase = int.MaxValue;

    internal static async Task ThenCompleteAsync(Guid requestId, Chunk[] original, int next, Chunk rejected)
    {
        var shape = new OnlineTextMaintenanceStreamShape(requestId);
        for (var prefix = First; prefix < next; prefix++)
        {
            shape.Admit(original[prefix], FirstProgressSequence + prefix);
        }
        var completed = shape.Completed;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => shape.Admit(rejected, FirstProgressSequence + next));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(error.Message).IsEqualTo(GrainRoutingProtocol.InvalidRequest);
        await Assert.That(shape.Completed).IsEqualTo(completed);
        for (var suffix = next; suffix < original.Length; suffix++)
        {
            shape.Admit(original[suffix], FirstProgressSequence + suffix);
        }
        await Assert.That(shape.Completed).IsTrue();
    }

    internal static IEnumerable<Chunk> Corruptions(Chunk frame)
    {
        var progress = frame.ProgressResult!.Value.Value!;
        foreach (var corrupted in EnvelopeCorruptions(frame))
        {
            yield return corrupted;
        }
        foreach (var corrupted in PayloadCorruptions(frame, progress))
        {
            yield return corrupted;
        }
    }

    private static IEnumerable<Chunk> EnvelopeCorruptions(Chunk frame)
    {
        yield return frame with
        {
            Kind = CqrsStreamChunkKind.Started
        };
        yield return frame with
        {
            Sequence = frame.Sequence + Step
        };
        yield return frame with
        {
            Sequence = null
        };
        yield return frame with
        {
            Message = WrongMessage
        };
        yield return frame with
        {
            EventType = WrongEvent
        };
        yield return frame with
        {
            EventId = WrongEventId
        };
        yield return frame with
        {
            ProgressResult = null
        };
        yield return frame with
        {
            Final = Result<GrainOperationReply>.Succeed(new GrainOperationReply())
        };
        yield return frame with
        {
            ProgressResult = Result<GrainRequestProgress>.Fail(
            Errors.Problem(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest))
        };
    }

    private static IEnumerable<Chunk> PayloadCorruptions(Chunk frame, GrainRequestProgress progress)
    {
        yield return Payload(frame, progress with
        {
            OnlineTextPhase = null
        });
        yield return Payload(frame, progress with
        {
            OnlineTextPhase = (OnlineTextPhase)InvalidPhase
        });
        yield return Payload(frame, progress with
        {
            RequestId = Guid.NewGuid()
        });
        yield return Payload(frame, progress with
        {
            AnnPhase = AnnMaintenancePhase.Configure
        });
        yield return Payload(frame, progress with
        {
            TextPhase = TextIndexMaintenancePhase.Configure
        });
    }

    private static Chunk Payload(Chunk frame, GrainRequestProgress progress)
        => frame with
        {
            ProgressResult = Result<GrainRequestProgress>.Succeed(progress)
        };
}
