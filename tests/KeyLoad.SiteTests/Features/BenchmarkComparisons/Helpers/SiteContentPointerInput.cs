using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteContentPointerInput
{
    private const string Dispatch = "Input.dispatchMouseEvent";
    private const string Type = "type";
    private const string Pressed = "mousePressed";
    private const string Released = "mouseReleased";
    private const string X = "x";
    private const string Y = "y";
    private const string Button = "button";
    private const string Left = "left";
    private const string ClickCount = "clickCount";
    private const string MissingTarget = "The native pointer target is not visible in the current viewport.";
    private const int One = 1;

    internal static async Task ClickAsync(SiteBrowserCdpClient cdp, string selector, CancellationToken token)
    {
        var literal = JsonSerializer.Serialize(selector);
        var script = "(() => { const target=document.querySelector(" + literal +
            "); if (!target) return null; const r=target.getBoundingClientRect(); const x=r.left+r.width/2;" +
            " const y=r.top+r.height/2; return r.width>0&&r.height>0&&x>=0&&x<innerWidth&&y>=0&&y<innerHeight" +
            "&&target.contains(document.elementFromPoint(x,y)) ? {x,y} : null; })()";
        var point = await cdp.EvaluateAsync(script, false, token);
        if (point.ValueKind != JsonValueKind.Object)
        { throw new InvalidOperationException(MissingTarget); }
        var parameters = new Dictionary<string, object?>
        {
            [Type] = Pressed,
            [X] = point.GetProperty(X).GetDouble(),
            [Y] = point.GetProperty(Y).GetDouble(),
            [Button] = Left,
            [ClickCount] = One,
        };
        await cdp.CommandAsync(Dispatch, parameters, token);
        parameters[Type] = Released;
        await cdp.CommandAsync(Dispatch, parameters, token);
    }
}
