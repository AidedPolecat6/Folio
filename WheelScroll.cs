namespace Folio;

internal sealed class WheelScroll
{
    private long? lastEvent;
    private int direction;
    public (double Offset, int PageDelta) Step(int delta, double offset, double maximum, double speed, long now)
    {
        if (delta == 0) return (offset, 0);
        var sign = delta < 0 ? 1 : -1;
        // A pause separates wheel/trackpad gestures; momentum cannot run through several pages.
        var fresh = lastEvent == null || now - lastEvent >= 220 || direction != sign;
        lastEvent = now; direction = sign;
        var atEdge = sign > 0 ? offset >= maximum - 0.5 : offset <= 0.5;
        if (atEdge) return (offset, fresh ? sign : 0);
        return (Math.Clamp(offset - delta / 120.0 * 48 * speed, 0, maximum), 0);
    }
}
