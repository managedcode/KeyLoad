using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core.Features.Search;

internal static class OnlineTextNativeAuthorityPurpose
{
    internal static string For(OperationKind kind)
        => kind == OperationKind.OnlineTextPublicationPhase
            ? OnlineTextPublicationProtocol.NativePurpose : NativeAuthorityContract.Purpose;
}
