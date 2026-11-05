using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Folio;

// Only the visible pages and one neighbour on each side own image controls.
internal sealed class ContinuousSurface : Canvas
{
    private readonly ScrollViewer scroll;
    private readonly Grid activePaper;
    private readonly Image activeImage;
    private readonly Func<PdfEngine> getEngine;
    private readonly Action<int> changed;
    private readonly Dictionary<int, Image> images = new();
    private readonly Dictionary<int, int> renderedWidths = new();
    private bool rendering, pending;
    private int revision, active;
    private PdfEngine? engine;
    private double? pendingOffset;
    public double ReadingOffset => Layout == null ? 0 : (pendingOffset ?? scroll.VerticalOffset) - Layout.Tops[active];
    public PageLayout? Layout { get; private set; }
    public int ImageCount => images.Count;
    public ContinuousSurface(ScrollViewer scroll, Grid paper, Image image, Func<PdfEngine> getEngine, Action<int> changed)
    {
        this.scroll = scroll; activePaper = paper; activeImage = image; this.getEngine = getEngine; this.changed = changed;
        Background = Ui.Canvas;
        scroll.ScrollChanged += (_, _) => { if (scroll.Content == this && Layout != null) UpdateViewport(); };
        PreviewMouseDown += (_, e) =>
        {
            if (Layout == null) return;
            SetActive(Layout.PageAt(e.GetPosition(this).Y));
        };
    }
    public void Configure(Size[] sizes, double zoom, int page, double readingOffset)
    {
        revision++; engine = getEngine(); Layout = new PageLayout(sizes, zoom, Math.Max(1, scroll.ActualWidth - 18));
        Children.Clear(); images.Clear(); renderedWidths.Clear(); activeImage.Source = null;
        Width = Layout.Width; Height = Layout.Height; active = page;
        Children.Add(activePaper); PositionPaper();
        pendingOffset = Math.Max(0, Layout.Tops[page] + readingOffset); scroll.ScrollToVerticalOffset(pendingOffset.Value);
        UpdateViewport(page);
    }
    public void Jump(int page, double offset = 0)
    {
        if (Layout == null) return;
        SetActive(page); pendingOffset = Math.Max(0, Layout.Tops[page] + offset); scroll.ScrollToVerticalOffset(pendingOffset.Value); UpdateViewport(page);
    }
    private void PositionPaper()
    {
        var size = Layout!.Sizes[active];
        activePaper.Margin = new Thickness(0); activePaper.Width = size.Width * Layout.Zoom; activePaper.Height = size.Height * Layout.Zoom;
        SetLeft(activePaper, (Width - activePaper.Width) / 2); SetTop(activePaper, Layout.Tops[active]);
    }
    private void SetActive(int page)
    {
        if (page == active || Layout == null) return;
        var old = new Image { Source = activeImage.Source, Stretch = Stretch.Fill };
        images[active] = old; Children.Add(old); PositionImage(active, old);
        activeImage.Source = null;
        if (images.Remove(page, out var next)) { activeImage.Source = next.Source; Children.Remove(next); }
        active = page; PositionPaper(); changed(page);
    }
    private void PositionImage(int page, Image image)
    {
        image.Width = Layout!.Sizes[page].Width * Layout.Zoom; image.Height = Layout.Sizes[page].Height * Layout.Zoom;
        SetLeft(image, (Width - image.Width) / 2); SetTop(image, Layout.Tops[page]);
    }
    private void UpdateViewport(int? requested = null)
    {
        if (Layout == null) return;
        if (pendingOffset.HasValue && Math.Abs(scroll.ExtentHeight - Height) < 1 && Math.Abs(scroll.VerticalOffset - Math.Min(pendingOffset.Value, scroll.ScrollableHeight)) < 1) pendingOffset = null;
        var page = requested ?? (pendingOffset.HasValue ? active : Layout.PageAt(scroll.VerticalOffset + Math.Min(80, scroll.ViewportHeight / 4)));
        if (!activePaper.IsMouseCaptureWithin) SetActive(page);
        var keep = Layout.Visible(requested.HasValue ? Layout.Tops[page] : scroll.VerticalOffset, Math.Max(1, scroll.ViewportHeight)).Append(active).ToHashSet();
        foreach (var index in images.Keys.Where(i => !keep.Contains(i)).ToArray())
        { Children.Remove(images[index]); images.Remove(index); renderedWidths.Remove(index); }
        foreach (var index in keep)
        {
            if (index == active || images.ContainsKey(index)) continue;
            var image = new Image { Stretch = Stretch.Fill }; images[index] = image; Children.Add(image); PositionImage(index, image);
        }
        pending = true; RenderVisible();
    }
    private async void RenderVisible()
    {
        if (rendering) return;
        rendering = true;
        try
        {
            while (pending && Layout != null && scroll.Content == this)
            {
                pending = false; var version = revision; var source = engine!;
                foreach (var page in images.Keys.Prepend(active).Distinct().ToArray())
                {
                    if (Layout == null || version != revision || source != getEngine()) break;
                    var width = Math.Clamp((int)(Layout.Sizes[page].Width * Layout.Zoom * VisualTreeHelper.GetDpi(this).DpiScaleX), 100, 3000);
                    if (renderedWidths.GetValueOrDefault(page) == width) continue;
                    var bitmap = await Task.Run(() => source.Render(page, width));
                    if (version != revision || source != getEngine() || scroll.Content != this) break;
                    if (page == active) activeImage.Source = bitmap;
                    else if (images.TryGetValue(page, out var image)) image.Source = bitmap;
                    else continue;
                    renderedWidths[page] = width;
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception e) { ToolTip = "Could not render page: " + e.Message; }
        finally { rendering = false; if (pending && Layout != null && scroll.Content == this) RenderVisible(); }
    }
    public void Release()
    {
        revision++; pending = false; pendingOffset = null; Layout = null; Children.Clear(); images.Clear(); renderedWidths.Clear(); activeImage.Source = null;
    }
}
