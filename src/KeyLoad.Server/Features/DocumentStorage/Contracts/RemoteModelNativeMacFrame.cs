namespace KeyLoad.Server.Features.DocumentStorage;

[GenerateSerializer, Alias(RemoteModelNativeProtocol.MacFrameAlias)]
internal sealed record RemoteModelNativeMacFrame(
    [property: Id(0)] string Purpose,
    [property: Id(1)] byte[] Digest);
