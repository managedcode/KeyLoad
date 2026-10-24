using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Identifies the actual original producer within the same execution owner.</summary>
internal interface IRequestWorkBinding : IResourceLifetimeWorkBinding
{
    RequestWorkLease OriginalProducer
    {
        get;
    }
}
