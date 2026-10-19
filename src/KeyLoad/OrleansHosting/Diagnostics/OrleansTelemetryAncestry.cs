using System.Diagnostics;

namespace KeyLoad.Server.Features.OrleansHosting.Diagnostics;

/// <summary>Bounds local activity ancestry before the execution inherited baggage getter is accessed.</summary>
internal static class OrleansTelemetryAncestry
{
    private const int InitialParentCount = 0;

    internal static bool IsWithinLimit(Activity activity, int maximumParentLinks)
    {
        var examined = InitialParentCount;
        for (var parent = activity.Parent; parent is not null; parent = parent.Parent)
        {
            if (++examined > maximumParentLinks)
            {
                return false;
            }
        }
        return true;
    }
}
