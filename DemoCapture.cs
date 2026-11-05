using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Folio;

// Generates only fictional documents and uses a separate profile; never reads a user's PDFs.
internal static class DemoCapture
{
    public static void Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var folder = Path.Combine(Path.GetTempPath(), "Folio-demo-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        var guide = Path.Combine(folder, "Study guide.pdf"); var examples = Path.Combine(folder, "Worked examples.pdf");
        CreatePdf(guide, "Study guide", "A clear view of your next idea."); CreatePdf(examples, "Worked examples", "Compare. Understand. Remember.");
        var library = new UserLibrary(Path.Combine(folder, "library.json"));
        library.Data.Recent.Add(new(@"C:\Study\Study guide.pdf", new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero), true));
        library.Data.Recent.Add(new(@"C:\Study\Worked examples.pdf", new DateTimeOffset(2026, 11, 1, 11, 0, 0, TimeSpan.Zero)));
        library.Data.Shortcuts.Add(new(@"C:\Study", "Study materials")); library.Data.PdfFolders.Add(new(folder, "Demo PDFs")); library.Save();
        var window = new MainWindow(Path.Combine(folder, "workspace.json")) { Width = 1400, Height = 950, ShowInTaskbar = false, ShowActivated = false };
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(600); Capture(window, Path.Combine(output, "home.png"));
                window.Open(guide); var first = window.Current!; window.Open(examples); window.Merge(window.Current!, first);
                await Ready(window); Capture(window, Path.Combine(output, "tab-groups.png"));
                window.Detach(first); first.ToggleSplit(); first.Top.SetZoom(0.7); first.Bottom!.SetZoom(0.7); first.Bottom.Go(2);
                await Ready(window); Capture(window, Path.Combine(output, "split-view.png"));
                first.ToggleSplit(); first.Top.Go(1); first.Top.SetZoom(1.0);
                await Ready(window); Capture(window, Path.Combine(output, "bookmarks.png"));
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(output, "capture-error.txt"), e.ToString()); Environment.ExitCode = 1; }
            finally { window.Close(); app.Shutdown(); }
        };
        app.Run(window); Directory.Delete(folder, true);
    }
    private static async Task Ready(MainWindow window)
    {
        await Task.Delay(150);
        for (var i = 0; i < 100; i++)
        {
            var images = Descendants(window).OfType<Image>().ToArray();
            if (images.Length > 0 && images.All(x => x.Source != null)) { await Task.Delay(100); return; }
            await Task.Delay(50);
        }
        throw new TimeoutException("Demo pages did not finish rendering.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
        { var child = VisualTreeHelper.GetChild(item, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Capture(Window window, string path)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void CreatePdf(string path, string title, string subtitle)
    {
        using var doc = new PdfDocument(); doc.Info.Author = "Folio demo"; doc.Info.Title = title;
        var headings = new[] { "Overview", "Core concepts", "Worked example", "Reference notes", "Review checklist", "Next steps" };
        for (var i = 0; i < headings.Length; i++)
        {
            var page = doc.AddPage(); page.Width = XUnit.FromPoint(520); page.Height = XUnit.FromPoint(720);
            doc.Outlines.Add(headings[i], page);
            using var gfx = XGraphics.FromPdfPage(page);
            var blue = new XSolidBrush(XColor.FromArgb(38, 92, 141)); var ink = new XSolidBrush(XColor.FromArgb(35, 43, 55));
            gfx.DrawRectangle(blue, 0, 0, 520, 14);
            gfx.DrawString("FOLIO  /  DEMO DOCUMENT", new XFont("Arial", 10), blue, 42, 60);
            gfx.DrawString(i == 0 ? title : headings[i], new XFont("Arial", 30, XFontStyleEx.Bold), ink, 42, 120);
            gfx.DrawString(subtitle, new XFont("Arial", 14), ink, 42, 150);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(232, 241, 249)), 42, 190, 436, 120);
            gfx.DrawString("SPACE TO THINK", new XFont("Arial", 12, XFontStyleEx.Bold), blue, 60, 218);
            gfx.DrawString("Keep your references together.", new XFont("Arial", 16), ink, 60, 250);
            gfx.DrawString("Read at your own pace. Mark what matters.", new XFont("Arial", 12), ink, 60, 278);
            gfx.DrawString(headings[i], new XFont("Arial", 19, XFontStyleEx.Bold), ink, 42, 365);
            var lines = new[] { "01   Explore the main idea", "02   Compare the evidence", "03   Add a bookmark for later", "04   Review the example", "05   Connect the details" };
            for (var n = 0; n < lines.Length; n++)
            {
                gfx.DrawString(lines[n], new XFont("Arial", 13), ink, 54, 402 + n * 40);
                gfx.DrawLine(new XPen(XColor.FromArgb(220, 226, 232)), 42, 416 + n * 40, 478, 416 + n * 40);
            }
            gfx.DrawString($"Fictional sample • No personal data                       {i + 1} / {headings.Length}", new XFont("Arial", 9), blue, 42, 680);
            if (i == 1)
            {
                var annotation = new SquareAnnotation(doc) { Rectangle = new PdfRectangle(new XPoint(45, 290), new XPoint(475, 335)), Color = XColors.OrangeRed };
                var border = new PdfDictionary(doc); border.Elements.SetReal("/W", 2); annotation.Elements["/BS"] = border; page.Annotations.Add(annotation);
            }
        }
        doc.Save(path);
    }
}
