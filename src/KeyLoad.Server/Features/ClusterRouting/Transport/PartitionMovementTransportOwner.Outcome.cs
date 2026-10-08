using System.Security.Cryptography;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementTransportOwner
{
    internal async Task<PartitionMovementTransportReply> ExchangeOutcomeAsync(int index,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementOutcomeTransportRequest query,
        byte[] body, bool local, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PartitionMovementTransportReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var request = CreateOutcomeRequest(receiver.Endpoints[index], body);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var response = await resources.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    reply = await ReadReplyPurposeAsync(response, local, outcome: true, cancellationToken).ConfigureAwait(false);
                    await VerifyReplyAsync(index, receiver, query.Original with
                    { Envelope = query.Original.Envelope with { ExpiresAt = query.ExpiresAt, Nonce = query.Nonce } },
                        reply, local, cancellationToken).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }

    private HttpRequestMessage CreateOutcomeRequest(string endpoint, byte[] body)
    {
        var key = Convert.FromBase64String(options.PeerSecret);
        string signature;
        try
        {
            using var mac = new PartitionMovementMac(key, partition.Database.Limits.MaxBatchBytes);
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
}
