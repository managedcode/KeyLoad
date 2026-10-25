using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.OrleansExecution;
using Microsoft.AspNetCore.Http;
using P = KeyLoad.FunctionalTests.ClientApi.SdkProblemBoundaryProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>Controlled Kestrel framing of genuine Orleans replies, not production HTTP authentication/emission.</summary>
internal sealed class SdkProblemBoundaryEndpoint(SdkProblemBoundaryScenario scenario,
    SdkProblemBoundaryTasks tasks, bool declared)
{
    internal Task HandleAsync(HttpContext context) => tasks.Retain(HandleCoreAsync(context));
    private async Task HandleCoreAsync(HttpContext context)
    {
        if (context.Request.Headers[P.Authorization].SingleOrDefault() != P.Bearer + P.Secret
            || scenario.Execution.Fixture.Database.Database.Authenticate(P.Secret,
                scenario.Execution.Fixture.Clock.GetUtcNow()) != scenario.Principal.Id)
        {
            throw new InvalidOperationException(P.Failure);
        }
        var reply = context.Request.Path == P.Commands ? await CommandAsync(context) : await ReadAsync(context);
        byte[] bytes;
        if (reply.Error is { } code)
        {
            await SdkProblemBoundaryAssertions.RefusalAsync(reply);
            bytes = JsonDefaults.Serialize(Errors.Problem(code, reply.SafeDetail!));
            await Assert.That(bytes.AsSpan().SequenceEqual(JsonDefaults.Serialize(SdkProblemBoundaryScenario.ExpectedProblem))).IsTrue();
            context.Response.StatusCode = Errors.Status(code);
            context.Response.ContentType = P.ProblemType;
        }
        else
        {
            bytes = context.Request.Path == P.Commands
                ? JsonDefaults.Serialize(ConnectionAssertions.Value<CommitReceipt>(reply))
                : JsonDefaults.Serialize(new DocumentReadResult(ConnectionAssertions.Value<DocumentResult>(reply)));
            context.Response.ContentType = P.JsonType;
        }
        if (declared)
        {
            context.Response.ContentLength = bytes.Length;
        }
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private async Task<GrainOperationReply> CommandAsync(HttpContext context)
    {
        var command = await JsonSerializer.DeserializeAsync<CommandRequest>(context.Request.Body,
            JsonDefaults.Options, context.RequestAborted) ?? throw new InvalidOperationException(P.Failure);
        var expected = command.CommandId == scenario.Denied.CommandId ? scenario.Denied : scenario.Healthy;
        await SdkProblemBoundaryAssertions.EqualAsync(expected, command);
        await Assert.That(context.Request.Headers[P.CommandHeader].SingleOrDefault()).IsEqualTo(command.CommandId.ToString());
        return await scenario.CommandAsync(command, context.RequestAborted);
    }

    private async Task<GrainOperationReply> ReadAsync(HttpContext context)
    {
        await Assert.That(context.Request.Path.Value).IsEqualTo(P.Documents);
        var read = await JsonSerializer.DeserializeAsync<GetDocumentRequest>(context.Request.Body,
            JsonDefaults.Options, context.RequestAborted) ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(read.Reference == scenario.Forbidden || read.Reference == scenario.Allowed).IsTrue();
        return await scenario.ReadAsync(read.Reference, context.RequestAborted);
    }
}
