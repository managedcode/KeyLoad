using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns effect transport responsibility while borrowing the original HTTP, native address pins and configured authority.</summary>
internal sealed class PartitionMovementEffectTransport(PartitionMovementTransportContext context, PartitionMovementTransportReplyVerifier replyVerifier)
{
    internal async Task<PartitionMovementAuthenticatedReply> ExchangeOutcomeAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementOutcomeTransportRequest query,
        byte[] body, bool local, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PartitionMovementAuthenticatedReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var request = CreateOutcomeRequest(receiver.Endpoints[index], body);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var response = await context.SendOutcomeRequestAsync(request, cancellationToken).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    reply = await replyVerifier.ReadReplyPurposeAsync(response, local, outcome: true, cancellationToken).ConfigureAwait(false);
                    await replyVerifier.VerifyReplyAsync(index, receiver, query.Original with
                    { Envelope = query.Original.Envelope with { ExpiresAt = query.ExpiresAt, Nonce = query.Nonce } },
                        reply.Value, local, PartitionMoveOriginalDispatchIdentity.Digest(query.Original.CommandId, query.Original.Envelope),
                        cancellationToken).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }

    internal HttpRequestMessage CreateOutcomeRequest(string endpoint, byte[] body)
    {
        var key = Convert.FromBase64String(context.Options.PeerSecret);
        string signature;
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            signature = mac.SignOutcome(body, reply: false);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint), PartitionMovementProtocol.OutcomePath));
        try
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new(PartitionMovementProtocol.ContentType);
            request.Headers.Add(PartitionMovementProtocol.SignatureHeader, signature);
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

    internal async Task<PartitionMovementAuthenticatedReply> ExchangeAsync(int index, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementTransportRequest original, byte[] body, bool local, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PartitionMovementAuthenticatedReply? terminal = null;
        var submitted = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var request = CreateRequest(receiver.Endpoints[index], body);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                submitted = true;
                using var response = await context.Resources.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    terminal = await replyVerifier.ReadReplyAsync(response, local, cancellationToken).ConfigureAwait(false);
                    await replyVerifier.VerifyReplyAsync(index, receiver, original, terminal.Value, local,
                        PartitionMoveOriginalDispatchIdentity.Digest(original.CommandId, original.Envelope), cancellationToken).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        try
        { ServerFailureObserver.ThrowIfAny(failures); }
        catch (Exception error) when (submitted && PartitionMovementTransportContext.IsUnresolvedTransport(error))
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, PartitionMovementProtocol.Unavailable);
            unknown.Data[PartitionMovementTransportContext.OriginalFailureKey] = error;
            throw unknown;
        }
        return terminal ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }

    internal HttpRequestMessage CreateRequest(string endpoint, byte[] body)
    {
        var key = Convert.FromBase64String(context.Options.PeerSecret);
        string signature;
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            signature = mac.Sign(body, reply: false);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint), PartitionMovementProtocol.Path));
        try
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new(PartitionMovementProtocol.ContentType);
            request.Headers.Add(PartitionMovementProtocol.SignatureHeader, signature);
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
