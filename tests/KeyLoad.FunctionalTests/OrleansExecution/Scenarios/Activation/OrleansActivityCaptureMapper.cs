using System.Diagnostics;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class OrleansActivityCaptureMapper
{
    internal static OrleansActivityCapture From(Activity activity)
        => new(activity.Source.Name, activity.DisplayName, activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(), activity.ParentSpanId.ToHexString(), activity.Status,
            activity.StatusDescription, activity.TraceStateString,
            activity.Baggage.Select(static item => item.Key + "=" + item.Value).ToArray(),
            activity.Links.Any(),
            activity.TagObjects.Select(static tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())).ToArray(),
            activity.Events.Select(static activityEvent => new OrleansActivityEventCapture(activityEvent.Name,
                activityEvent.Tags.Select(static tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())).ToArray())).ToArray());
}
