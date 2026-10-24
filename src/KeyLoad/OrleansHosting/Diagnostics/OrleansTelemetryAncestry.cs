using System.Diagnostics;

namespace KeyLoad.Server.OrleansHosting.Diagnostics;

/// <summary>Bounds local activity ancestry before the execution inherited baggage getter is accessed.</summary>
internal static class OrleansTelemetryAncestry
{
    internal static bool IsWithinLimit(Activity activity, int maximumParentLinks)
    {
        var examined = 0;
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
