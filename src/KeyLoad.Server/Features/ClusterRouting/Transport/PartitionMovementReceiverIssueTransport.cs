namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns issue transport responsibility while borrowing the original HTTP, native address pins and configured authority.</summary>
internal sealed class PartitionMovementReceiverIssueTransport(PartitionMovementTransportContext context, PartitionMovementTransportReplyVerifier replyVerifier)
{
    internal async Task<PartitionMovementAuthenticatedReply> ExchangeReceiverIssueAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementReceiverExchange exchange,
        byte[] body, bool local, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PartitionMovementAuthenticatedReply? terminal = null;
        var submitted = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var request = CreateReceiverIssueRequest(receiver.Endpoints[index], exchange, body);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                submitted = true;
                using var response = exchange.Query
                    ? await context.SendOutcomeRequestAsync(request, cancellationToken).ConfigureAwait(false)
                    : await context.Resources.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    terminal = await replyVerifier.ReadReplyMacPurposeAsync(response, local, PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.ReceiverIssue,
                        cancellationToken).ConfigureAwait(false);
                    await replyVerifier.VerifyReplyAsync(index, receiver, exchange.ReplyScope, terminal.Value, local,
                        exchange.OriginalPhaseIdentityDigest, cancellationToken).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        try
        { ServerFailureObserver.ThrowIfAny(failures); }
        catch (Exception error) when (!exchange.Query && submitted && PartitionMovementTransportContext.IsUnresolvedTransport(error))
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, PartitionMovementProtocol.Unavailable);
            unknown.Data[PartitionMovementTransportContext.OriginalFailureKey] = error;
            throw unknown;
        }
        return terminal ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }

    internal static HttpRequestMessage CreateReceiverIssueRequest(string endpoint,
        PartitionMovementReceiverExchange exchange, byte[] body)
    {
        var path = exchange.Query ? PartitionMovementProtocol.ReceiverIssueProofPath : PartitionMovementProtocol.ReceiverIssuePath;
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint), path));
        try
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new(PartitionMovementProtocol.ContentType);
            request.Headers.Add(PartitionMovementProtocol.SignatureHeader, exchange.RequestSignature);
            return request;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(request.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task<PartitionMovementAuthenticatedReply> ExchangeRetireCancellationAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementReceiverExchange exchange,
        byte[] body, bool local, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PartitionMovementAuthenticatedReply? terminal = null;
        var submitted = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var request = CreateRetireCancellationRequest(receiver.Endpoints[index], exchange, body);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                submitted = true;
                using var response = exchange.Query
                    ? await context.SendOutcomeRequestAsync(request, cancellationToken).ConfigureAwait(false)
                    : await context.Resources.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    terminal = await replyVerifier.ReadReplyMacPurposeAsync(response, local, PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.RetireCancellation,
                        cancellationToken).ConfigureAwait(false);
                    await replyVerifier.VerifyReplyAsync(index, receiver, exchange.ReplyScope, terminal.Value, local,
                        exchange.OriginalPhaseIdentityDigest, cancellationToken).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        try
        { ServerFailureObserver.ThrowIfAny(failures); }
        catch (Exception error) when (!exchange.Query && submitted && PartitionMovementTransportContext.IsUnresolvedTransport(error))
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, PartitionMovementProtocol.Unavailable);
            unknown.Data[PartitionMovementTransportContext.OriginalFailureKey] = error;
            throw unknown;
        }
        return terminal ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }

    internal static HttpRequestMessage CreateRetireCancellationRequest(string endpoint,
        PartitionMovementReceiverExchange exchange, byte[] body)
    {
        var path = exchange.Query ? PartitionMovementRetireCancellationProtocol.QueryPath : PartitionMovementRetireCancellationProtocol.CommandPath;
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint), path));
        try
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new(PartitionMovementProtocol.ContentType);
            request.Headers.Add(PartitionMovementProtocol.SignatureHeader, exchange.RequestSignature);
            return request;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(request.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
}
