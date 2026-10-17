using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Identifies the actual original producer within the same native owner.</summary>
internal interface INativeRequestWorkBinding : IResourceLifetimeWorkBinding
{
    NativeRequestWorkLease OriginalProducer
    {
        get;
    }
}
