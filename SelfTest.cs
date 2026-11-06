using System.Diagnostics;
using System.IO;
using System.Windows;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;

namespace Folio;

internal static class SelfTest
{
    public static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "folio-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var report = Path.Combine(Path.GetTempPath(), "opencode", "folio-selftest.txt");
        var results = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        try
        {
            var path = Path.Combine(folder, "source.pdf");
            using (var doc = new PdfDocument())
            {
                var p = doc.AddPage(); p.Width = XUnit.FromPoint(400); p.Height = XUnit.FromPoint(600);
                doc.AddPage();
                using (var gfx = XGraphics.FromPdfPage(p)) gfx.DrawRectangle(XPens.Blue, 20, 20, 100, 60);
                doc.Outlines.Add("First", p, true).Outlines.Add("Second", doc.Pages[1]);
                p.Annotations.Add(PdfLinkAnnotation.CreateDocumentLink(new PdfRectangle(new XPoint(20, 400), new XPoint(120, 450)), 2));
                p.Annotations.Add(PdfLinkAnnotation.CreateWebLink(new PdfRectangle(new XPoint(150, 400), new XPoint(300, 450)), "https://example.com/"));
                doc.Save(path);
            }
            using var file = new PdfFile(path);
            Check(file.Engine.Count == 2, "Open multi-page PDF");
            var image = file.Engine.Render(0, 800);
            Check(image.PixelWidth == 800 && image.PixelHeight == 1200, "Native page rendering");
            var linkBounds = file.Engine.LinkBounds(0, 800, 1200);
            Check(linkBounds.Count == 2 && linkBounds[0].Contains(new Point(140, 350)), "Visible link bounds match clickable areas");
            var linkPixel = new byte[4]; image.CopyPixels(new Int32Rect(40, 300, 1, 1), linkPixel, 4, 0);
            Check(linkPixel[0] == 205 && linkPixel[1] == 125 && linkPixel[2] == 45 && !file.Dirty, "Link frames render without modifying PDF");
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 1000; i++) Check(ReferenceEquals(image, file.Engine.Render(0, 800)), "cache");
            watch.Stop(); results.RemoveAll(x => x == "PASS: cache");
            results.Add($"PASS: 1000 cached page requests: {watch.Elapsed.TotalMilliseconds:F2} ms");
            Check(file.Engine.Link(0, new Point(140, 350), 800, 1200)?.Page == 1, "Internal link destination");
            Check(file.Engine.Link(0, new Point(400, 350), 800, 1200)?.Url == "https://example.com/", "External web link");
            file.Edit(d => d.Outlines[0].Title = "Renamed");
            Check(file.Dirty && file.Document.Outlines[0].Title == "Renamed", "Rename bookmark and mark dirty");
            file.Undo(); Check(file.Document.Outlines[0].Title == "First" && !file.Dirty, "Undo restores saved state");
            file.Undo(true); Check(file.Document.Outlines[0].Title == "Renamed", "Redo bookmark change");
            file.Edit(d =>
            {
                var outline = d.Outlines.Add("Temporary", d.Pages[1]);
                d.Outlines.Remove(outline); d.Outlines[0].Outlines.Add(outline);
                d.Outlines[0].Outlines.Remove(outline);
            });
            Check(file.Document.Outlines.Count == 1 && file.Document.Outlines[0].Outlines.Count == 1, "Add, nest and remove bookmark");
            file.Edit(d =>
            {
                var a = new SquareAnnotation(d) { Rectangle = new PdfRectangle(new XPoint(40, 100), new XPoint(200, 200)), Color = XColors.Red };
                var border = new PdfDictionary(d); border.Elements.SetReal("/W", 4); a.Elements["/BS"] = border;
                d.Pages[0].Annotations.Add(a);
            });
            var annotated = file.Engine.Render(0, 800);
            var pixels = new byte[800 * 1200 * 4]; annotated.CopyPixels(pixels, 800 * 4, 0);
            Check(Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 2] > 200 && pixels[i * 4] < 80 && pixels[i * 4 + 1] < 80), "Square annotation visibly renders");
            var output = Path.Combine(folder, "saved.pdf"); file.Save(output);
            using (var reopen = new PdfFile(output))
            {
                Check(reopen.Document.Outlines[0].Title == "Renamed" && reopen.Document.Outlines[0].Outlines[0].Title == "Second", "Bookmark hierarchy survives save/reopen");
                Check(reopen.Document.Pages[0].Annotations.Count == 3, "Links and rectangle survive save/reopen");
                Check(reopen.Engine.Link(0, new Point(140, 350), 800, 1200)?.Page == 1, "Internal link survives save/reopen");
            }
            File.AppendAllText(output, "\n%external change");
            var protectedWrite = false;
            try { file.Save(output); } catch (IOException) { protectedWrite = true; }
            Check(protectedWrite, "External modifications cannot be overwritten silently");
            using (var tab = new DocumentTab(file))
            {
                tab.ToggleSplit();
                Check(tab.Bottom != null && ReferenceEquals(tab.Top.File, tab.Bottom.File), "Split views share one document");
                tab.Bottom!.Go(1); tab.Bottom.SetZoom(1.5);
                Check(tab.Top.Page == 0 && tab.Bottom.Page == 1 && tab.Top.Zoom != tab.Bottom.Zoom, "Split page position and zoom are independent");
                tab.ToggleSplit(); Check(tab.Bottom == null && tab.Active == tab.Top, "Closing split retains original view");
            }
            file.Edit(d => d.Pages[0].Rotate = 90);
            var point = new Point(70, 120);
            var device = file.Engine.ConvertPoint(0, point, 1200, 800, false);
            var roundtrip = file.Engine.ConvertPoint(0, device, 1200, 800, true);
            Check((point - roundtrip).Length < 1, "Rotated-page annotation coordinate roundtrip");
            var rect = new Rect(20, 30, 80, 100);
            for (var handle = 0; handle < 8; handle++)
            {
                var resized = RectangleGeometry.Resize(rect, handle, RectangleGeometry.Handle(rect, handle) + new Vector(7, 9), new Size(400, 600));
                Check(resized != rect && resized.Width >= 3 && resized.Height >= 3, $"Resize handle {handle} changes geometry");
            }
            Check(RectangleGeometry.Resize(rect, 0, new Point(500, 500), new Size(400, 600)).Size == new Size(3, 3), "Resize cannot invert rectangle");
            Check(RectangleGeometry.Resize(rect, 4, new Point(500, 700), new Size(400, 600)).BottomRight == new Point(400, 600), "Resize stays within page bounds");
            var originalBounds = file.Document.Pages[0].Annotations[2].Rectangle;
            file.Edit(d =>
            {
                var a = d.Pages[0].Annotations[2];
                a.Rectangle = new PdfRectangle(new XPoint(15, 70), new XPoint(230, 250)); a.Elements.Remove("/AP");
            });
            Check(file.Document.Pages[0].Annotations.Count == 3 && file.Document.Pages[0].Annotations[2].Rectangle.Width == 215, "Resizing modifies existing rectangle without adding an annotation");
            file.Undo(); Check(file.Document.Pages[0].Annotations[2].Rectangle == originalBounds, "Rectangle resize undo restores original bounds");
            file.Undo(true);
            var resizedPath = Path.Combine(folder, "resized.pdf"); file.Save(resizedPath);
            using (var saved = new PdfFile(resizedPath)) Check(saved.Document.Pages[0].Annotations[2].Rectangle.Width == 215, "Resized rectangle survives save and reopen");
            using (var first = new DocumentTab(file))
            using (var second = new DocumentTab(file, 1))
            using (var third = new DocumentTab(file))
            using (var sidebar = new BookmarkSidebar())
            {
                var group = new TabGroup(); group.Add(first); group.Add(second); group.Add(third);
                Check(group.Tabs.Count == 3 && group.ColumnDefinitions.Count == 5 && group.Children.Count == 5, "Three grouped PDFs create independent columns and two dividers");
                group.SetWeights(new[] { 2.0, 1.0, 3.0 });
                Check(group.ColumnDefinitions[0].Width.Value == 2 && group.ColumnDefinitions[4].Width.Value == 3, "Group width ratios restore");
                group.Remove(second); var detached = new TabGroup(); detached.Add(second);
                Check(group.Tabs.Count == 2 && detached.Tabs.Count == 1 && second.Top.Page == 1, "Ungroup preserves PDF reading position");
                sidebar.Bind(first); Check(sidebar.DocumentName == Path.GetFileName(file.Path), "Shared bookmark sidebar binds active PDF");
                var tree = sidebar.Children.OfType<System.Windows.Controls.TreeView>().Single();
                var bookmarkText = (System.Windows.Controls.TextBlock)((System.Windows.Controls.TreeViewItem)tree.Items[0]).Header;
                bookmarkText.Text = "A long bookmark title that needs several lines in a narrow sidebar";
                var sidebarHost = new Window { Content = sidebar, Width = 240, Height = 600, ShowActivated = false, ShowInTaskbar = false };
                sidebarHost.Show(); sidebarHost.UpdateLayout();
                Check(bookmarkText.ActualHeight > bookmarkText.FontSize * 2 && bookmarkText.ActualWidth < tree.ActualWidth, "Long bookmarks wrap within a narrow sidebar");
                sidebar.TextSize = 18; sidebarHost.UpdateLayout();
                Check(bookmarkText.FontSize == 18, "Bookmark text inherits adjusted size");
                sidebarHost.Content = null; sidebarHost.Close();
                sidebar.Bind(null); Check(sidebar.DocumentName == "Select a PDF", "Shared sidebar clears on close");
                group.Remove(third); group.Remove(first); detached.Remove(second);
            }
            var workspacePath = Path.Combine(folder, "workspace.json");
            var main = new MainWindow(workspacePath);
            Check(main.IsHomeVisible && main.Current == null, "Folio starts on home without automatically loading PDFs");
            main.Open(path); var firstTab = main.Current!;
            main.Open(resizedPath, 1); var secondTab = main.Current!;
            main.Open(output); var thirdTab = main.Current!;
            main.Merge(secondTab, firstTab); main.Merge(thirdTab, firstTab);
            Check(main.CaptureWorkspace().Tabs.Select(t => t.Group).Distinct().Count() == 1 && main.CaptureWorkspace().Tabs.Length == 3, "Window groups three PDF tabs into one workspace");
            main.Detach(secondTab);
            Check(main.CaptureWorkspace().Tabs.Select(t => t.Group).Distinct().Count() == 2 && secondTab.Top.Page == 1, "Window detaches tab without resetting its page");
            main.Current!.ToggleSplit();
            var snapshot = main.CaptureWorkspace(); main.Close();
            var untouchedWorkspace = File.ReadAllText(workspacePath);
            var homeOnly = new MainWindow(workspacePath); homeOnly.Close();
            Check(File.ReadAllText(workspacePath) == untouchedWorkspace, "Closing home without opening PDFs preserves the previous workspace");
            var restoredWindow = new MainWindow(workspacePath); restoredWindow.RestoreWorkspace();
            var restoredState = restoredWindow.CaptureWorkspace();
            Check(restoredState.Tabs.Length == 3 && restoredState.Tabs.Select(t => t.Group).SequenceEqual(snapshot.Tabs.Select(t => t.Group)), "Workspace restores grouped and independent tabs in order");
            Check(restoredWindow.Current?.File.Path == resizedPath && restoredWindow.Current.Bottom?.Page == 1, "Workspace restores active document and top/bottom split");
            var currentCount = restoredState.Tabs.Length; restoredWindow.RestoreWorkspace();
            Check(restoredWindow.CaptureWorkspace().Tabs.Length == currentCount, "Repeated restore does not duplicate tabs");
            restoredWindow.ShowHome();
            Check(restoredWindow.IsHomeVisible && restoredWindow.CaptureWorkspace().Tabs.Length == currentCount && restoredWindow.CaptureWorkspace().SidebarVisible, "Home retains open tabs and the bookmark-sidebar preference");
            restoredWindow.Open(resizedPath);
            Check(!restoredWindow.IsHomeVisible && restoredWindow.Current?.File.Path == resizedPath, "Opening a PDF directly leaves home and activates its tab");
            restoredWindow.Close();
            var libraryPath = Path.Combine(folder, "test-library.json");
            var library = new UserLibrary(libraryPath);
            var homeLayout = new HomePage(library, _ => { }, () => { });
            var homeHost = new Window { Content = homeLayout, Width = 1400, Height = 800, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                homeHost.Show(); homeHost.UpdateLayout();
                var topRow = ((System.Windows.Controls.StackPanel)homeLayout.Content).Children.OfType<System.Windows.Controls.Grid>().Single();
                var leftCard = (FrameworkElement)topRow.Children[0]; var rightCard = (FrameworkElement)topRow.Children[1];
                Check(System.Windows.Controls.Grid.GetColumn(rightCard) == 1 && leftCard.ActualWidth > rightCard.ActualWidth * 1.8, "Wide Home places recent PDFs and shortcuts side by side at roughly 2:1");
                homeHost.Width = 800; homeHost.UpdateLayout();
                Check(System.Windows.Controls.Grid.GetRow(rightCard) == 1 && Math.Abs(leftCard.ActualWidth - rightCard.ActualWidth) < 1, "Narrow Home stacks recent PDFs and shortcuts at full width");
                homeHost.Width = 1400; homeHost.UpdateLayout();
                Check(System.Windows.Controls.Grid.GetRow(rightCard) == 0 && System.Windows.Controls.Grid.GetColumn(rightCard) == 1, "Home restores side-by-side layout when widened");
            }
            finally { homeHost.Content = null; homeHost.Close(); }
            Check(library.Data.ScrollSpeed == 0.5, "Default scroll speed is 50 percent");
            library.Opened(path); library.Data.Recent[0] = library.Data.Recent[0] with { Pinned = true }; library.Save(); library.Opened(path);
            Check(library.Data.Recent.Count == 1 && library.Data.Recent[0].Pinned, "Recent PDFs deduplicate and retain pins when reopened");
            library.Data.ScrollSpeed = 0.3; library.Data.BookmarkTextSize = 18; library.Data.Shortcuts.Add(new(folder, "Exams")); library.Data.PdfFolders.Add(new(folder, "PDFs")); library.Save();
            var persisted = new UserLibrary(libraryPath);
            Check(persisted.Data.BookmarkTextSize == 18, "Bookmark text size persists");
            Check(persisted.Data.ScrollSpeed == 0.3 && persisted.Data.Shortcuts[0].Name == "Exams" && persisted.Data.PdfFolders[0].Path == folder, "Scroll setting, folder shortcuts and PDF panels persist");
            persisted.Data.Recent.Clear(); persisted.Save(); Check(File.Exists(path), "Removing a recent PDF never deletes the document");
            File.WriteAllText(Path.Combine(folder, "ignore.txt"), "not a PDF");
            File.Copy(path, Path.Combine(folder, "UPPER.PDF")); Directory.CreateDirectory(Path.Combine(folder, "nested")); File.Copy(path, Path.Combine(folder, "nested", "hidden.pdf"));
            var listed = UserLibrary.PdfFiles(folder);
            Check(listed.Any(p => p.EndsWith("UPPER.PDF")) && listed.All(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) && !listed.Any(p => p.EndsWith("hidden.pdf")), "Folder panel includes only direct PDFs, including uppercase extensions");
            var wheel = new WheelScroll();
            Check(wheel.Step(-120, 0, 100, 0.5, 0).Offset == 24, "Wheel speed is halved at 50 percent");
            Check(wheel.Step(-30, 24, 100, 0.5, 20).Offset == 30, "Small wheel/trackpad deltas remain proportional");
            Check(wheel.Step(-120, 95, 100, 0.5, 40) == (100.0, 0), "Scrolling reaches the edge without changing page");
            Check(wheel.Step(-120, 100, 100, 0.5, 70).PageDelta == 0 && wheel.Step(-120, 100, 100, 0.5, 130).PageDelta == 0, "Continuing momentum cannot skip pages");
            Check(wheel.Step(-120, 100, 100, 0.5, 400).PageDelta == 1, "A new gesture after a pause advances one page");
            Check(wheel.Step(120, 0, 100, 0.5, 700).PageDelta == -1, "Upward gesture at the top goes to the previous page");
            var layout = new PageLayout(new[] { new Size(400, 600), new Size(600, 400), new Size(400, 900) }, 1, 800);
            Check(layout.Tops.SequenceEqual(new[] { 18.0, 636.0, 1054.0 }) && layout.PageAt(1100) == 2, "Continuous layout supports mixed page sizes with gaps");
            Check(layout.Visible(650, 300).SequenceEqual(new[] { 0, 1, 2 }), "Only visible pages and neighbours are requested");
            var bigPath = Path.Combine(folder, "long.pdf");
            using (var longPdf = new PdfDocument()) { for (var i = 0; i < 100; i++) longPdf.AddPage(); longPdf.Save(bigPath); }
            using (var longFile = new PdfFile(bigPath))
            using (var reader = new PageView(longFile))
            {
                var host = new Window { Content = reader, Width = 700, Height = 700, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    host.Show(); Wait(() => (reader.Scroll.Content as ContinuousSurface)?.Layout != null);
                    var surface = (ContinuousSurface)reader.Scroll.Content;
                    reader.Scroll.ScrollToVerticalOffset(surface.Layout!.Tops[50] + 100);
                    Wait(() => reader.Page == 50);
                    Check(surface.ImageCount <= 6, "100-page PDF keeps only nearby image controls alive");
                    reader.Go(2); Wait(() => Math.Abs(reader.Scroll.VerticalOffset - surface.Layout!.Tops[2]) < 2);
                    Check(reader.Page == 2, "Go to page navigates the continuous surface");
                    reader.Go(40); reader.SetZoom(0.8);
                    Wait(() => Math.Abs(reader.Scroll.VerticalOffset - surface.Layout!.Tops[40]) < 2);
                    Check(reader.Page == 40, "Zoom immediately after navigation preserves the requested page");
                    reader.Go(2); Wait(() => reader.Page == 2 && Math.Abs(reader.Scroll.VerticalOffset - surface.Layout!.Tops[2]) < 2);
                    reader.Continuous = false; Wait(() => reader.Scroll.Content is System.Windows.Controls.Grid);
                    Check(reader.Page == 2, "Switching to single-page mode preserves page position");
                    reader.Continuous = true; Wait(() => (reader.Scroll.Content as ContinuousSurface)?.Layout != null);
                    Check(reader.Page == 2, "Switching back to continuous mode preserves page position");
                    reader.SetZoom(2); Wait(() => reader.Scroll.ScrollableWidth > 100);
                    reader.Scroll.ScrollToHorizontalOffset(100); Wait(() => Math.Abs(reader.Scroll.HorizontalOffset - 100) < 1);
                    reader.ScrollHorizontally(120); Wait(() => Math.Abs(reader.Scroll.HorizontalOffset - 124) < 1);
                    reader.ScrollHorizontally(-60); Wait(() => Math.Abs(reader.Scroll.HorizontalOffset - 112) < 1);
                    Check(true, "Horizontal gestures preserve direction, proportional deltas and scroll speed");
                }
                finally { host.Content = null; host.Close(); }
            }
            File.WriteAllLines(report, results);
        }
        catch (Exception e) { File.WriteAllLines(report, results.Append(e.ToString())); Environment.ExitCode = 1; }
        finally { Directory.Delete(folder, true); }
    }
    private static void Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew(); var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) => { if (ready() || clock.ElapsedMilliseconds > 8000) frame.Continue = false; };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
        if (!ready()) throw new TimeoutException("UI rendering check timed out.");
    }
}
