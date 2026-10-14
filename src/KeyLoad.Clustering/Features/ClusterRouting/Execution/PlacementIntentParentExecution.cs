using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class PlacementIntentParentExecution
{
    internal static async Task<PlacementIntentV1> ExecuteAsync(DecodedGrainRequest original,
        IServiceProvider services, TimeProvider clock,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        var request = GrainNativePayload.ReadCommand<ApplyPlacementIntentRequest>(original.Payload);
        var database = services.GetRequiredService<DatabaseEngine>();
        var principal = GrainRequestAuthority.ReloadForRequest(database, original.Envelope, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var executor = services.GetService<IPlacementIntentExecutor>()
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest);
        var work = new ReadExecutionBudget(services.GetRequiredService<IOptions<DatabaseLimits>>(), clock, writer.CancellationToken);
        work.ConstrainLifetime(original.Envelope.ExpiresAt);
        return await executor.ApplyAsync(original.Envelope, request, work, intent =>
        {
            var last = intent.Receipts.Last();
            return writer.ProgressAsync(new GrainRequestProgress(original.Envelope.RequestId)
            {
                PlacementIntent = new(intent.IntentId, intent.Revision, intent.CompletedMoves,
                    intent.Plan.Moves.Length, last.MoveId, last.Phase)
            }, PlacementIntentNativeProtocol.Progress);
        }, writer.CancellationToken).ConfigureAwait(true);
    }
}
