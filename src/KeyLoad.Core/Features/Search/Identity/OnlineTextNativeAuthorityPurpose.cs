using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core.Features.Search;

internal static class OnlineTextNativeAuthorityPurpose
{
    internal static string For(OperationKind kind)
        => kind switch
        {
            OperationKind.Bm25CorpusPublicationPhase => Bm25CorpusProtocol.NativePurpose,
            OperationKind.OnlineTextPublicationPhase => OnlineTextPublicationProtocol.NativePurpose,
            _ => NativeAuthorityContract.Purpose
        };
}
