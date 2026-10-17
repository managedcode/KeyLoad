using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorAuthenticatedSourcePages
{
    private const long InitialPosition = 0;
    private const int MinimumLimit = 1;
    private const int FirstOrdinal = 0;

    internal static ImmutableArray<EventSourcePage> Read(EventVectorMacCheckedReply authenticatedReply,
        EventVectorRemoteCall originalCall, EventVectorRemoteEncoding encoding, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(authenticatedReply);
        ArgumentNullException.ThrowIfNull(originalCall);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(admission);
        if (originalCall.OriginalNativeRequest.IsEmpty
            || originalCall.OriginalNativeRequest.Length > encoding.MaximumBytes
            || authenticatedReply.OriginalReplyBytes.Length > encoding.MaximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, EventVectorRemoteProtocol.Invalid);
        }
        var originalRequest = NativeSerialization.Deserialize<EventVectorSourceReadCall>(
            originalCall.OriginalNativeRequest.Span);
        var actual = authenticatedReply.Reply;
        if (originalCall.Action != EventVectorRemoteAction.SourceRead
            || actual.Action != originalCall.Action || actual.RequestId != originalCall.RequestId
            || actual.Nonce != originalCall.Nonce || actual.Error is not null
            || actual.OriginalNativeReply.IsEmpty || originalRequest is null || originalRequest.OriginalReadRequest is not { } reads
            || reads.Requests.IsDefault || authenticatedReply.OriginalReplyBytes.IsEmpty
            || string.IsNullOrWhiteSpace(authenticatedReply.OriginalSignature))
        {
            throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
        }
        admission.RequireEncodedBytes(actual.OriginalNativeReply.Length);
        var pages = NativeSerialization.Deserialize<ImmutableArray<EventSourcePage>>(actual.OriginalNativeReply.Span);
        if (pages.IsDefault || pages.Length != reads.Requests.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
        }
        admission.RequireEntryCount(pages.Length);
        for (var ordinal = FirstOrdinal; ordinal < pages.Length; ordinal++)
        {
            Require(reads.Requests[ordinal], pages[ordinal]);
        }
        return pages;
    }

    private static void Require(ReadEventSourceRequest original, EventSourcePage page)
    {
        if (original is null || original.Source is null || original.Limit < MinimumLimit
            || original.AfterPosition < InitialPosition || page is null || page.Source != original.Source
            || page.Head is null || page.Events.IsDefault || page.Events.Length > original.Limit
            || page.Head.Generation != original.Source.Generation || page.CutPosition < InitialPosition
            || page.Head.TailPosition < original.AfterPosition || string.IsNullOrEmpty(page.Cursor))
        {
            throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
        }
        var position = original.AfterPosition;
        foreach (var value in page.Events)
        {
            if (value is null || value.Source != original.Source || value.Position <= position
                || value.Position > page.Head.TailPosition || value.Data is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
            }
            position = value.Position;
        }
    }
}
