using System.Windows;

namespace Folio;

internal sealed class PageLayout
{
    public Size[] Sizes { get; }
    public double[] Tops { get; }
    public double Zoom { get; }
    public double Width { get; }
    public double Height { get; }
    public PageLayout(Size[] sizes, double zoom, double viewportWidth)
    {
        Sizes = sizes; Zoom = zoom; Tops = new double[sizes.Length];
        var y = 18.0;
        for (var i = 0; i < sizes.Length; i++) { Tops[i] = y; y += sizes[i].Height * zoom + 18; }
        Height = y; Width = Math.Max(viewportWidth, sizes.Max(s => s.Width * zoom) + 36);
    }
    public int PageAt(double y)
    {
        var index = Array.BinarySearch(Tops, y);
        return Math.Clamp(index >= 0 ? index : ~index - 1, 0, Sizes.Length - 1);
    }
    public IEnumerable<int> Visible(double offset, double height)
    {
        var first = Math.Max(0, PageAt(offset) - 1); var last = Math.Min(Sizes.Length - 1, PageAt(offset + height) + 1);
        for (var i = first; i <= last; i++) yield return i;
    }
}
