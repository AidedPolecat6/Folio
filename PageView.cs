using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;

namespace Folio;

internal sealed class SquareAnnotation : PdfAnnotation
{
    public SquareAnnotation(PdfDocument document) : base(document)
    { Elements.SetName("/Subtype", "/Square"); Flags = PdfAnnotationFlags.Print; }
}

internal sealed class PageView : DockPanel, IDisposable
{
    public PdfFile File { get; }
    public int Page { get; private set; }
    public double Zoom { get; private set; } = 1;
    public double ScrollSpeed { get; set; } = 0.5;
    private bool continuous = true;
    private readonly ContinuousSurface pages;
    private PdfEngine? sizeEngine;
    private Size[]? pageSizes;
    private double? restoreOffset;
    public double ReadingOffset => continuous && pages.Layout != null ? pages.ReadingOffset : Scroll.VerticalOffset;
    public bool Continuous
    {
        get => continuous;
        set
        {
            if (continuous == value) return;
            var offset = ReadingOffset; continuous = value; request++;
            Scroll.Content = null; pages.Release(); paper.Margin = new Thickness(value ? 0 : 18);
            Scroll.Content = value ? pages : paper; restoreOffset = offset; Refresh();
        }
    }
    public void RestorePosition(ViewState saved) { Page = Math.Clamp(saved.Page, 0, File.Engine.Count - 1); fit = ""; Zoom = Math.Clamp(saved.Zoom, 0.2, 5); restoreOffset = saved.Scroll; Refresh(); }
    private readonly WheelScroll wheel = new();
    public event Action<PageView>? Activated;
    public event Action<int>? Navigated;
    private readonly Image image = new() { Stretch = Stretch.Fill };
    private readonly Canvas overlay = new() { Background = Brushes.Transparent };
    private readonly Grid paper = new() { Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18) };
    public ScrollViewer Scroll { get; } = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Ui.Canvas };
    private readonly TextBox pageBox = new() { Width = 42, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2) };
    private readonly TextBlock count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3) };
    private readonly TextBlock status = new() { Margin = new Thickness(8, 4, 8, 4), Foreground = Ui.Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ToggleButton draw = new() { Content = "□ Rectangle", ToolTip = "Draw a rectangle. Turn off to follow links or select existing rectangles.", Margin = new Thickness(2), Padding = new Thickness(6) };
    private readonly ComboBox colors = new() { Width = 78, Margin = new Thickness(2) };
    private readonly ComboBox widths = new() { Width = 50, Margin = new Thickness(2) };
    private static string lastColor = "Red";
    private static double lastWidth = 2;
    private int request;
    private bool disposed, changingStyle;
    private string fit = "width";
    private readonly Stack<int> back = new(), forward = new();
    private Point? anchor;
    private Rect startRect;
    private readonly Thumb[] handles = new Thumb[8];
    private readonly WrapPanel rectangleTools = new() { Margin = new Thickness(4, 0, 4, 0), Visibility = Visibility.Collapsed };
    private int selected = -1;
    private readonly Rectangle ghost = new() { Stroke = Ui.Accent, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 2 }, IsHitTestVisible = false };
    private readonly System.Windows.Threading.DispatcherTimer resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public PageView(PdfFile file, int page = 0)
    {
        File = file; Page = Math.Clamp(page, 0, file.Engine.Count - 1);
        pages = new ContinuousSurface(Scroll, paper, image, () => File.Engine, p => { Page = p; ClearSelection(); pageBox.Text = (p + 1).ToString(); });
        LastChildFill = true;
        Background = Ui.Surface;
        var toolbar = new WrapPanel { Background = Ui.Surface, Margin = new Thickness(4) };
        toolbar.Children.Add(Ui.Button("←", "Previous link location (Alt+Left)", () => History(false)));
        toolbar.Children.Add(Ui.Button("→", "Next link location (Alt+Right)", () => History(true)));
        toolbar.Children.Add(Ui.Divider());
        toolbar.Children.Add(Ui.Button("‹", "Previous page", () => Go(Page - 1)));
        toolbar.Children.Add(pageBox); toolbar.Children.Add(count);
        toolbar.Children.Add(Ui.Button("›", "Next page", () => Go(Page + 1)));
        toolbar.Children.Add(Ui.Divider());
        toolbar.Children.Add(Ui.Button("−", "Zoom out", () => SetZoom(Zoom / 1.2)));
        toolbar.Children.Add(Ui.Button("+", "Zoom in", () => SetZoom(Zoom * 1.2)));
        toolbar.Children.Add(Ui.Button("Width", "Fit width", () => { fit = "width"; Refresh(); }));
        toolbar.Children.Add(Ui.Button("Page", "Fit page", () => { fit = "page"; Refresh(); }));
        toolbar.Children.Add(Ui.Divider());
        toolbar.Children.Add(draw);
        foreach (var c in new[] { "Red", "Blue", "Green", "Orange", "Black", "Purple" }) colors.Items.Add(c);
        colors.SelectedItem = lastColor; colors.ToolTip = "Rectangle border color";
        foreach (var w in new[] { 0.5, 1, 2, 3, 4, 6, 8 }) widths.Items.Add(w);
        widths.SelectedItem = lastWidth; widths.ToolTip = "Border thickness in PDF points";
        rectangleTools.Children.Add(colors); rectangleTools.Children.Add(widths);
        rectangleTools.Children.Add(Ui.Button("Delete", "Delete selected rectangle", DeleteRectangle));
        SetDock(rectangleTools, Dock.Top);
        SetDock(toolbar, Dock.Top); Children.Add(toolbar);
        Children.Add(rectangleTools);
        SetDock(status, Dock.Bottom); Children.Add(status);
        paper.Children.Add(image); paper.Children.Add(overlay); overlay.Children.Add(ghost); ghost.Visibility = Visibility.Hidden;
        CreateHandles();
        Scroll.Content = pages; Children.Add(Scroll);
        pageBox.ToolTip = "Type a page number and press Enter · Ctrl+G";
        System.Windows.Automation.AutomationProperties.SetName(pageBox, "Current page");
        pageBox.KeyDown += (_, e) => { if (e.Key == Key.Enter && int.TryParse(pageBox.Text, out var n)) { Go(n - 1); e.Handled = true; } };
        PreviewMouseDown += (_, _) => Activated?.Invoke(this);
        GotKeyboardFocus += (_, _) => Activated?.Invoke(this);
        overlay.MouseLeftButtonDown += MouseDownPage;
        overlay.MouseMove += MouseMovePage;
        overlay.MouseLeftButtonUp += MouseUpPage;
        overlay.LostMouseCapture += (_, e) => { if (e.OriginalSource == overlay) anchor = null; };
        draw.Checked += (_, _) => { ClearSelection(); rectangleTools.Visibility = Visibility.Visible; overlay.Cursor = Cursors.Cross; };
        draw.Unchecked += (_, _) => { rectangleTools.Visibility = selected >= 0 ? Visibility.Visible : Visibility.Collapsed; overlay.Cursor = Cursors.Arrow; };
        colors.SelectionChanged += (_, _) => StyleChanged(); widths.SelectionChanged += (_, _) => StyleChanged();
        Scroll.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SetZoom(Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1)); e.Handled = true; }
            else if (continuous)
            { Scroll.ScrollToVerticalOffset(Scroll.VerticalOffset - e.Delta / 120.0 * 48 * ScrollSpeed); e.Handled = true; }
            else
            {
                var step = wheel.Step(e.Delta, Scroll.VerticalOffset, Scroll.ScrollableHeight, ScrollSpeed, Environment.TickCount64);
                if (step.PageDelta > 0 && Page < File.Engine.Count - 1) Go(Page + 1);
                else if (step.PageDelta < 0 && Page > 0) { Go(Page - 1); Scroll.ScrollToBottom(); }
                else Scroll.ScrollToVerticalOffset(step.Offset);
                e.Handled = true;
            }
        };
        File.Changed += Changed;
        Loaded += (_, _) => Refresh();
        SizeChanged += (_, _) => { if (fit != "") { resizeTimer.Stop(); resizeTimer.Start(); } };
        resizeTimer.Tick += (_, _) => { resizeTimer.Stop(); Refresh(); };
    }
    private void Changed()
    {
        if (selected >= File.Document.Pages[Page].Annotations.Count) selected = -1;
        if (selected >= 0 && File.Document.Pages[Page].Annotations[selected].Elements.GetName("/Subtype") == "/Square")
            ShowGhost(Bounds(File.Document.Pages[Page].Annotations[selected]));
        else ClearSelection();
        Refresh();
    }
    public void Go(int page, bool history = true)
    {
        page = Math.Clamp(page, 0, File.Engine.Count - 1);
        if (history && page != Page) { back.Push(Page); forward.Clear(); }
        Page = page; ClearSelection();
        if (continuous && pages.Layout != null) { pages.Jump(page); pageBox.Text = (page + 1).ToString(); }
        else { restoreOffset = 0; Scroll.ScrollToTop(); Refresh(); }
        Navigated?.Invoke(Page);
    }
    public void History(bool next)
    {
        var from = next ? forward : back; var to = next ? back : forward;
        if (from.Count == 0) return;
        to.Push(Page); Go(from.Pop(), false);
    }
    public void SetZoom(double zoom) { fit = ""; Zoom = Math.Clamp(zoom, 0.2, 5); Refresh(); }
    public void Jump()
    {
        var input = Ui.Prompt("Go to page", (Page + 1).ToString());
        if (int.TryParse(input, out var page)) Go(page - 1);
    }
    public async void Search()
    {
        var query = Ui.Prompt("Find text — next matching page", "");
        if (query == null) return;
        status.Text = "Searching…";
        var engine = File.Engine; var start = Page;
        try
        {
            var match = await Task.Run(() =>
            {
                for (var offset = 1; offset <= engine.Count; offset++)
                {
                    var index = (start + offset) % engine.Count;
                    if (engine.Text(index).Contains(query, StringComparison.OrdinalIgnoreCase)) return index;
                }
                return -1;
            });
            if (disposed || engine != File.Engine) return;
            if (match >= 0) { Go(match); status.Text = $"Found “{query}” on page {match + 1}"; }
            else status.Text = $"No match for “{query}” (scanned pages need OCR).";
        }
        catch (ObjectDisposedException) { }
        catch (Exception e) { status.Text = e.Message; }
    }
    public async void Refresh()
    {
        if (disposed || !IsLoaded || Scroll.ActualWidth < 50) return;
        var ticket = ++request;
        overlay.IsHitTestVisible = false;
        try
        {
            var engine = File.Engine; var page = Page;
            if (continuous)
            {
                var offset = restoreOffset ?? ReadingOffset;
                if (sizeEngine != engine || pageSizes == null)
                {
                    var sizes = await Task.Run(engine.Sizes);
                    if (disposed || ticket != request || engine != File.Engine) return;
                    pageSizes = sizes; sizeEngine = engine;
                }
                var currentSize = pageSizes[page];
                if (fit != "")
                {
                    Zoom = Math.Max(0.1, (Scroll.ActualWidth - 58) / pageSizes.Max(s => s.Width));
                    if (fit == "page") Zoom = Math.Max(0.1, Math.Min(Zoom, (Scroll.ActualHeight - 40) / currentSize.Height));
                }
                pages.Configure(pageSizes, Zoom, page, offset); restoreOffset = null;
                pageBox.Text = (page + 1).ToString(); count.Text = $"/ {engine.Count}";
                overlay.IsHitTestVisible = true;
                if (selected >= 0) ShowGhost(Bounds(File.Document.Pages[Page].Annotations[selected]));
                status.Text = $"{Zoom:P0}  ·  Continuous scrolling  ·  Ctrl+wheel zoom";
                return;
            }
            var size = engine.Size(page);
            if (fit != "")
            {
                Zoom = Math.Max(0.1, (Scroll.ActualWidth - 58) / size.Width);
                if (fit == "page") Zoom = Math.Max(0.1, Math.Min(Zoom, (Scroll.ActualHeight - 40) / size.Height));
            }
            var w = Math.Max(1, size.Width * Zoom); var h = Math.Max(1, size.Height * Zoom);
            paper.Width = w; paper.Height = h;
            pageBox.Text = (page + 1).ToString(); count.Text = $"/ {engine.Count}";
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var bitmap = await Task.Run(() => engine.Render(page, (int)(w * dpi)));
            if (disposed || ticket != request) return;
            image.Source = bitmap;
            if (restoreOffset.HasValue) { Scroll.ScrollToVerticalOffset(restoreOffset.Value); restoreOffset = null; }
            overlay.IsHitTestVisible = true;
            if (selected >= 0) ShowGhost(Bounds(File.Document.Pages[Page].Annotations[selected]));
            status.Text = $"{Zoom:P0}  ·  Ctrl+B bookmark  ·  Ctrl+Shift+S split  ·  Ctrl+wheel zoom";
        }
        catch (ObjectDisposedException) { }
        catch (Exception e) { if (ticket == request) status.Text = e.Message; }
    }
    private Point Convert(Point point, bool toPdf) => File.Engine.ConvertPoint(Page, point, Math.Max(1, (int)paper.Width), Math.Max(1, (int)paper.Height), toPdf);
    private Rect Bounds(PdfAnnotation a)
    {
        var r = a.Rectangle;
        return new Rect(Convert(new(r.X1, r.Y1), false), Convert(new(r.X2, r.Y2), false));
    }
    private void ShowGhost(Rect r)
    {
        ghost.Width = r.Width; ghost.Height = r.Height; Canvas.SetLeft(ghost, r.Left); Canvas.SetTop(ghost, r.Top); ghost.Visibility = Visibility.Visible;
        for (var i = 0; i < handles.Length; i++)
        {
            var p = RectangleGeometry.Handle(r, i); Canvas.SetLeft(handles[i], p.X - 5); Canvas.SetTop(handles[i], p.Y - 5);
            handles[i].Visibility = selected >= 0 ? Visibility.Visible : Visibility.Hidden;
        }
        rectangleTools.Visibility = Visibility.Visible;
    }
    private void ClearSelection()
    {
        selected = -1; ghost.Visibility = Visibility.Hidden;
        foreach (var handle in handles) if (handle != null) handle.Visibility = Visibility.Hidden;
        rectangleTools.Visibility = draw.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }
    private void CreateHandles()
    {
        var names = new[] { "top left", "top", "top right", "right", "bottom right", "bottom", "bottom left", "left" };
        var cursors = new[] { Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE, Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE };
        for (var i = 0; i < 8; i++)
        {
            var index = i;
            var handle = new Thumb { Width = 10, Height = 10, Background = Ui.Accent, BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Cursor = cursors[i], Focusable = true, Visibility = Visibility.Hidden, ToolTip = $"Resize {names[i]} · arrow keys for precision" };
            System.Windows.Automation.AutomationProperties.SetName(handle, $"Resize rectangle {names[i]}");
            handles[i] = handle; overlay.Children.Add(handle);
            Rect initial = Rect.Empty; Vector delta = new();
            handle.DragStarted += (_, _) => { if (selected >= 0) { initial = Bounds(File.Document.Pages[Page].Annotations[selected]); delta = new(); } };
            handle.DragDelta += (_, e) =>
            {
                if (initial.IsEmpty || selected < 0) return;
                delta += new Vector(e.HorizontalChange, e.VerticalChange);
                ShowGhost(RectangleGeometry.Resize(initial, index, RectangleGeometry.Handle(initial, index) + delta, new Size(paper.Width, paper.Height)));
            };
            handle.DragCompleted += (_, e) =>
            {
                if (initial.IsEmpty || selected < 0) return;
                if (!e.Canceled && delta.Length >= 1) CommitRectangle(RectangleGeometry.Resize(initial, index, RectangleGeometry.Handle(initial, index) + delta, new Size(paper.Width, paper.Height)));
                else ShowGhost(initial);
                initial = Rect.Empty;
            };
            handle.PreviewKeyDown += (_, e) =>
            {
                if (selected < 0) return;
                var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                var move = e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), Key.Down => new Vector(0, step), _ => new Vector() };
                if (move.Length == 0) return;
                var r = Bounds(File.Document.Pages[Page].Annotations[selected]);
                CommitRectangle(RectangleGeometry.Resize(r, index, RectangleGeometry.Handle(r, index) + move, new Size(paper.Width, paper.Height))); e.Handled = true;
            };
        }
    }
    private void MouseDownPage(object sender, MouseButtonEventArgs e)
    {
        Ui.Run(() =>
        {
            var p = e.GetPosition(overlay);
            if (draw.IsChecked == true) { selected = -1; anchor = p; startRect = new(p, p); }
            else
            {
                var annotations = File.Document.Pages[Page].Annotations;
                selected = -1;
                for (var i = annotations.Count - 1; i >= 0; i--)
                {
                    if (annotations[i].Elements.GetName("/Subtype") != "/Square") continue;
                    var bounds = Bounds(annotations[i]); var hit = bounds; hit.Inflate(6, 6);
                    if (!hit.Contains(p)) continue;
                    selected = i; startRect = bounds; anchor = p;
                    changingStyle = true;
                    var ac = annotations[i].Color;
                    var hex = $"#{ac.R:X2}{ac.G:X2}{ac.B:X2}";
                    if (!colors.Items.Contains(hex)) colors.Items.Add(hex);
                    colors.SelectedItem = hex;
                    var thickness = annotations[i].Elements.GetDictionary("/BS")?.Elements.GetReal("/W") ?? 1;
                    if (!widths.Items.Contains(thickness)) widths.Items.Add(thickness);
                    widths.SelectedItem = thickness;
                    changingStyle = false;
                    ShowGhost(bounds); status.Text = "Drag a handle to resize · drag inside to move · Delete to remove";
                    break;
                }
                if (selected < 0)
                {
                    ClearSelection();
                    var link = File.Engine.Link(Page, p, (int)paper.Width, (int)paper.Height);
                    if (link is { Page: >= 0 } internalLink) Go(internalLink.Page);
                    else if (link?.Url is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http" || uri.Scheme == "mailto"))
                        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    return;
                }
            }
            overlay.CaptureMouse(); e.Handled = true;
        });
    }
    private Rect DragRect(Point p)
    {
        p = new(Math.Clamp(p.X, 0, paper.Width), Math.Clamp(p.Y, 0, paper.Height));
        if (selected < 0) return new Rect(anchor!.Value, p);
        var offset = p - anchor!.Value;
        return new Rect(new Point(Math.Clamp(startRect.X + offset.X, 0, Math.Max(0, paper.Width - startRect.Width)), Math.Clamp(startRect.Y + offset.Y, 0, Math.Max(0, paper.Height - startRect.Height))), startRect.Size);
    }
    private void MouseMovePage(object sender, MouseEventArgs e)
    { if (anchor != null && overlay.IsMouseCaptured) ShowGhost(DragRect(e.GetPosition(overlay))); }
    private void MouseUpPage(object sender, MouseButtonEventArgs e)
    {
        if (anchor == null) return;
        var p = e.GetPosition(overlay); var r = DragRect(p); var distance = (p - anchor.Value).Length;
        anchor = null; overlay.ReleaseMouseCapture();
        if (distance < 3 || r.Width < 3 || r.Height < 3) return;
        CommitRectangle(r);
    }
    private void CommitRectangle(Rect r)
    {
        var a = Convert(r.TopLeft, true); var b = Convert(r.BottomRight, true); var index = selected;
        Ui.Run(() => File.Edit(doc =>
        {
            var annotation = index >= 0 ? doc.Pages[Page].Annotations[index] : new SquareAnnotation(doc);
            annotation.Rectangle = new PdfRectangle(new XPoint(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new XPoint(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
            annotation.Elements.Remove("/AP");
            if (index < 0) { ApplyStyle(annotation); selected = doc.Pages[Page].Annotations.Count; doc.Pages[Page].Annotations.Add(annotation); }
        }));
        draw.IsChecked = false;
    }
    private void ApplyStyle(PdfAnnotation a)
    {
        var color = (Color)ColorConverter.ConvertFromString(lastColor);
        a.Color = XColor.FromArgb(color.R, color.G, color.B);
        var border = new PdfDictionary(File.Document); border.Elements.SetReal("/W", lastWidth); border.Elements.SetName("/S", "/S");
        a.Elements["/BS"] = border;
        a.Elements.Remove("/AP");
    }
    private void StyleChanged()
    {
        if (changingStyle || colors.SelectedItem == null || widths.SelectedItem == null) return;
        lastColor = colors.SelectedItem.ToString()!; lastWidth = (double)widths.SelectedItem;
        if (selected >= 0)
        {
            var index = selected;
            Ui.Run(() => File.Edit(doc => ApplyStyle(doc.Pages[Page].Annotations[index])));
        }
    }
    public void DeleteRectangle()
    {
        if (selected < 0) return;
        var index = selected; File.Edit(doc => doc.Pages[Page].Annotations.Remove(doc.Pages[Page].Annotations[index]));
    }
    public void Dispose() { disposed = true; request++; resizeTimer.Stop(); File.Changed -= Changed; pages.Release(); image.Source = null; }
}
