namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Closed native producer-disposal operation.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RequestCqrsProbeNativeProtocol.MethodAlias)]
internal enum RequestCqrsProbeNativeMethod
{
    /// <summary>Locates the original admitted producer claim.</summary>
    Locate = 0,
    /// <summary>Acknowledges disposal of that exact original producer.</summary>
    Notify = 1
}
