using System.Text;
using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.AspNetCore.Http;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeCredentialStatusProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeCredentialStatusEndpoint(NativeCredentialStatusState state)
{
    private int count;
    private readonly TaskCompletionSource<Task>[] handlers = Enumerable.Range(P.Empty, P.Requests)
        .Select(_ => new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
    private readonly int[] completedStatuses = new int[P.Requests];

    internal Task HandleAsync(HttpContext context)
    {
        var ordinal = Interlocked.Increment(ref count);
        if (ordinal > P.Requests)
        {
            throw new InvalidOperationException(P.Failure);
        }
        var actual = state.Tasks.Retain(HandleCoreAsync(context, (NativeCredentialStatusStep)ordinal));
        handlers[ordinal - P.First].TrySetResult(actual);
        return actual;
    }

    internal async Task JoinAsync(NativeCredentialStatusStep step, CancellationToken token)
    {
        await state.Tasks.JoinAsync(await handlers[(int)step - P.First].Task.WaitAsync(token));
        var expected = step switch
        {
            NativeCredentialStatusStep.OriginalBoolean => StatusCodes.Status200OK,
            NativeCredentialStatusStep.OrdinaryDenied => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status204NoContent
        };
        await Assert.That(completedStatuses[(int)step - P.First]).IsEqualTo(expected);
    }

    internal async Task RequireCountAsync()
        => await Assert.That(count).IsEqualTo(P.Requests);

    private async Task HandleCoreAsync(HttpContext context, NativeCredentialStatusStep step)
    {
        var denied = step == NativeCredentialStatusStep.OrdinaryDenied;
        var principal = denied ? state.Ordinary : state.Administrator;
        var secret = denied ? P.OriginalKey : P.CallerKey;
        await Assert.That(context.Request.Path.Value).IsEqualTo(P.Route);
        await Assert.That(context.Request.Headers[P.Authorization].SingleOrDefault() == P.Bearer + secret).IsTrue();
        await Assert.That(state.Scenario.Fixture.Database.Database.Authenticate(secret,
            state.Scenario.Fixture.Clock.GetUtcNow()) == principal.Id).IsTrue();
        var (id, key) = Select(step);
        var request = await JsonSerializer.DeserializeAsync<ConfigureApiKeyRequest>(context.Request.Body,
            JsonDefaults.Options, context.RequestAborted) ?? throw new InvalidOperationException(P.Failure);
        await NativeCredentialStatusAssertions.EqualAsync(new ConfigureApiKeyRequest(key), request);
        await Assert.That(context.Request.Headers[P.CommandHeader].SingleOrDefault()).IsEqualTo(id.ToString());
        var requestId = Guid.NewGuid();
        var signed = state.Scenario.Fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.ConfigureApiKey,
            id, NativeSerialization.Serialize(request));
        var actual = state.Tasks.Retain(state.Scenario.InvokeAsync(principal, requestId, id, signed, context.RequestAborted));
        var reply = await state.Tasks.CompleteAsync(actual);
        await state.Scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(context.RequestAborted);
        await WriteReplyAsync(context, step, reply);
        await context.Response.CompleteAsync();
        completedStatuses[(int)step - P.First] = context.Response.StatusCode;
    }

    private (Guid Id, ApiKeyRecord Key) Select(NativeCredentialStatusStep step)
        => step switch
        {
            NativeCredentialStatusStep.OrdinaryDenied => (state.DeniedId, state.Denied),
            NativeCredentialStatusStep.Healthy or NativeCredentialStatusStep.HealthyReplay => (state.HealthyId, state.Healthy),
            _ => (state.OriginalId, state.Original)
        };

    private static async Task WriteReplyAsync(HttpContext context, NativeCredentialStatusStep step, GrainOperationReply reply)
    {
        if (step == NativeCredentialStatusStep.OrdinaryDenied)
        {
            await Assert.That(reply.Error).IsEqualTo((ErrorCode?)ErrorCode.PermissionDenied);
            await Assert.That(reply.SafeDetail).IsEqualTo(P.DeniedDetail);
            await Assert.That(reply.Payload.IsEmpty).IsTrue();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Errors.Problem(ErrorCode.PermissionDenied, P.DeniedDetail), JsonDefaults.Options);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = P.ContentType;
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
            return;
        }
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        await Assert.That(ConnectionNativeAssertions.Value<bool>(reply)).IsTrue();
        await Assert.That(reply.Payload.Span.SequenceEqual(NativeSerialization.Serialize(new GrainValue(true)))).IsTrue();
        if (step == NativeCredentialStatusStep.OriginalBoolean)
        {
            var bytes = Encoding.UTF8.GetBytes(P.BooleanBody);
            context.Response.ContentType = P.ContentType;
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
            return;
        }
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
