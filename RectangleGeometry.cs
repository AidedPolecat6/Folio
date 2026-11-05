using System.Windows;

namespace Folio;

internal static class RectangleGeometry
{
    // Clockwise from top-left: corners and edge midpoints.
    public static Point Handle(Rect r, int index) => index switch
    {
        0 => r.TopLeft, 1 => new(r.Left + r.Width / 2, r.Top), 2 => r.TopRight,
        3 => new(r.Right, r.Top + r.Height / 2), 4 => r.BottomRight,
        5 => new(r.Left + r.Width / 2, r.Bottom), 6 => r.BottomLeft,
        7 => new(r.Left, r.Top + r.Height / 2), _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    public static Rect Resize(Rect r, int handle, Point p, Size page)
    {
        var x = Math.Clamp(p.X, 0, page.Width); var y = Math.Clamp(p.Y, 0, page.Height);
        var left = r.Left; var top = r.Top; var right = r.Right; var bottom = r.Bottom;
        if (handle is 0 or 6 or 7) left = Math.Min(x, right - 3);
        if (handle is 2 or 3 or 4) right = Math.Max(x, left + 3);
        if (handle is 0 or 1 or 2) top = Math.Min(y, bottom - 3);
        if (handle is 4 or 5 or 6) bottom = Math.Max(y, top + 3);
        return new Rect(new Point(left, top), new Point(right, bottom));
    }
}
