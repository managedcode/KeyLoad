using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Runs one signed control phase with its original bounded receiver metadata frame.</summary>
internal static class NativeResourceRetirementParentExecution
{
    internal static async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request,
        IServiceProvider services, TimeProvider clock, GrainRequestCodec codec,
        global::Orleans.Runtime.IGrainContext context,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        IResourceLifetimeWorkBinding binding, Action<Exception> observeOriginalFailure)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var token = writer.CancellationToken;
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(request.Envelope);
        if (!ResourceRetirementCommandScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementReadScope.InvalidRequest);
        }
        var database = services.GetRequiredService<DatabaseEngine>();
        var work = NativeCommandMetadataExecution.CreateWork(database, request, clock, binding, token);
        var coordinator = services.GetRequiredService<Func<ICommitCoordinator>>()();
        try
        {
            var result = await NativeCommandMetadataExecution.ExecuteAsync(database, coordinator, request,
                services, clock, codec, context, work, binding, token).ConfigureAwait(true);
            return NativeCommandMetadataReplies.Encode(request, result, work, codec.ExecutionOptions);
        }
        catch (KeyLoadException error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)
            && error.Code != ErrorCode.UnknownWriteOutcome && !work.HasIndeterminateMetadata)
        {
            observeOriginalFailure(error);
            var reply = GrainReplyFactory.Failure(error, command: true, diagnostics: null,
                codec.ExecutionOptions, request.Envelope.RequestId, cancellationToken: token);
            return reply.Error == ErrorCode.UnknownWriteOutcome ? reply
                : NativeCommandMetadataReplies.Attach(request, reply, work, codec.ExecutionOptions);
        }
    }
}
