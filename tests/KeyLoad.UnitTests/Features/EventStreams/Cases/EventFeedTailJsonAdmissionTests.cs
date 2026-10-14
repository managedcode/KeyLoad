using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.EventStreams;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class EventFeedTailJsonAdmissionTests
{
    private const int NoFrames = 0;
    private const int SingleTerminal = 1;
    private const long FirstSequence = 1;
    private const char OversizeCharacter = 'x';
    private const string HealthyDetail = "The original source was refused.";

    [Test]
    public async Task ActualNativeOversizeTerminalSettlesBeforeTypedQuotaThenHealthyOriginalRefusalRemainsComplete()
    {
        await using var services = Services(new DatabaseLimits());
        var context = new DefaultHttpContext { RequestServices = services };
        var routing = services.GetRequiredService<IOptions<GrainRoutingOptions>>().Value;
        var json = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value;
        var quotaChunk = CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>.Failed(
            Result<EventFeedControlResult>.Fail(Errors.Problem(ErrorCode.ResourceExhausted,
                "The event feed public stream exceeds its configured JSON grant.")), sequence: routing.MaximumTotalFrames);
        var terminalBytes = EventFeedTailJsonMeasurement.Measure(quotaChunk,
            checked((int)new DatabaseLimits().MaxQueryReadBytes), json.SerializerOptions);
        await using var boundedServices = Services(new DatabaseLimits { MaxQueryReadBytes = terminalBytes });
        var boundedContext = new DefaultHttpContext { RequestServices = boundedServices };
        var settled = false;
        var oversized = new string(OversizeCharacter, checked((int)(terminalBytes + terminalBytes)));
        var original = CqrsStream.Create<EventFeedTailProgress, EventFeedControlResult>(_ =>
        {
            try
            {
                return ValueTask.FromResult(Result<EventFeedControlResult>.Fail(Errors.Problem(ErrorCode.Validation, oversized)));
            }
            finally { settled = true; }
        });
        var frames = NoFrames;
        await foreach (var chunk in EventFeedTailPublicStream.AdmitAsync(boundedContext, original))
        {
            frames++;
            await Assert.That(settled).IsTrue();
            await Assert.That(chunk.Kind).IsEqualTo(CqrsStreamChunkKind.Failed);
            await Assert.That(chunk.Sequence).IsEqualTo(FirstSequence);
            await Assert.That(chunk.Problem!.ErrorCode).IsEqualTo(ErrorCode.ResourceExhausted.ToString());
            await Assert.That(chunk.Problem!.StatusCode).IsEqualTo(Errors.Status(ErrorCode.ResourceExhausted));
            await Assert.That(EventFeedTailJsonMeasurement.Measure(chunk, checked((int)terminalBytes), json.SerializerOptions))
                .IsLessThanOrEqualTo(terminalBytes);
        }
        await Assert.That(frames).IsEqualTo(SingleTerminal);
        await RequireHealthyAsync(context);
    }

    [Test]
    public async Task UnadmittableTerminalRefusesBeforeOriginalProducerThenAdequateGrantIsHealthy()
    {
        const long MinimumPositiveBytes = 1;
        await using var services = Services(new DatabaseLimits { MaxQueryReadBytes = MinimumPositiveBytes });
        var context = new DefaultHttpContext { RequestServices = services };
        var invoked = false;
        var source = CqrsStream.Create<EventFeedTailProgress, EventFeedControlResult>(_ =>
        {
            invoked = true;
            return ValueTask.FromResult(Result<EventFeedControlResult>.Fail(Errors.Problem(ErrorCode.Validation, HealthyDetail)));
        });
        try
        {
            _ = EventFeedTailPublicStream.AdmitAsync(context, source);
        }
        catch (KeyLoadException refusal) when (refusal.Code == ErrorCode.ResourceExhausted)
        {
            await Assert.That(invoked).IsFalse();
            await using var healthy = Services(new DatabaseLimits());
            await RequireHealthyAsync(new DefaultHttpContext { RequestServices = healthy });
            return;
        }
        throw new InvalidOperationException("The terminal reservation unexpectedly admitted its original producer.");
    }

    [Test]
    public async Task OriginalCallerCancellationJoinsProducerWithoutEmittingQuotaThenHealthyContinuation()
    {
        await using var services = Services(new DatabaseLimits());
        var context = new DefaultHttpContext { RequestServices = services };
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = false;
        var source = CqrsStream.Create<EventFeedTailProgress, EventFeedControlResult>(async writer =>
        {
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = writer.CancellationToken.Register(() => stopped.TrySetCanceled(writer.CancellationToken));
            try
            {
                entered.SetResult();
                await stopped.Task.ConfigureAwait(false);
            }
            finally { settled = true; }
            return Result<EventFeedControlResult>.Fail(Errors.Problem(ErrorCode.Validation, HealthyDetail));
        });
        await using var reader = EventFeedTailPublicStream.AdmitAsync(context, source, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        var originalMove = reader.MoveNextAsync().AsTask();
        var first = await Task.WhenAny(originalMove, entered.Task);
        if (first == originalMove)
        {
            await originalMove;
            throw new InvalidOperationException(HealthyDetail);
        }
        await cancellation.CancelAsync();
        try
        {
            await originalMove;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await Assert.That(settled).IsTrue();
            await RequireHealthyAsync(context);
            return;
        }
        throw new InvalidOperationException("The original cancellation was projected as an admitted Tail terminal.");
    }

    private static ServiceProvider Services(DatabaseLimits limits)
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IOptions<DatabaseLimits>>(Options.Create(limits));
        registrations.AddOptions<GrainRoutingOptions>();
        registrations.AddOptions<McpExecutionOptions>();
        registrations.AddOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>();
        return registrations.BuildServiceProvider();
    }

    private static async Task RequireHealthyAsync(HttpContext context)
    {
        var source = CqrsStream.Create<EventFeedTailProgress, EventFeedControlResult>(_ => ValueTask.FromResult(
            Result<EventFeedControlResult>.Fail(Errors.Problem(ErrorCode.Validation, HealthyDetail))));
        var frames = NoFrames;
        await foreach (var chunk in EventFeedTailPublicStream.AdmitAsync(context, source))
        {
            frames++;
            await Assert.That(chunk.Kind).IsEqualTo(CqrsStreamChunkKind.Failed);
            await Assert.That(chunk.Sequence).IsEqualTo(FirstSequence);
            await Assert.That(chunk.Problem!.ErrorCode).IsEqualTo(ErrorCode.Validation.ToString());
            await Assert.That(chunk.Problem!.Detail).IsEqualTo(HealthyDetail);
            await Assert.That(chunk.Problem!.StatusCode).IsEqualTo(Errors.Status(ErrorCode.Validation));
        }
        await Assert.That(frames).IsEqualTo(SingleTerminal);
    }
}
