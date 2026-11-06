using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Folio;

// PDFium is serialized across documents; native handles never outlive their owner.
internal sealed class PdfEngine : IDisposable
{
    internal static readonly object Gate = new();
    private IntPtr document;
    private GCHandle memory;
    private readonly Dictionary<(int, int), BitmapSource> cache = new();
    private readonly Queue<(int, int)> order = new();
    private long cacheBytes;
    public int Count { get; }
    public PdfEngine(byte[] bytes)
    {
        lock (Gate)
        {
            Native.Init();
            memory = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            document = Native.FPDF_LoadMemDocument64(memory.AddrOfPinnedObject(), (UIntPtr)bytes.Length, null);
            if (document == IntPtr.Zero)
            {
                memory.Free();
                throw new InvalidDataException("Cannot open this PDF. It may be damaged or password protected.");
            }
            Count = Native.FPDF_GetPageCount(document);
        }
    }
    private IntPtr Page(int index)
    {
        ObjectDisposedException.ThrowIf(document == IntPtr.Zero, this);
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        var page = Native.FPDF_LoadPage(document, index);
        if (page == IntPtr.Zero) throw new InvalidDataException("This page could not be loaded.");
        return page;
    }
    public Size Size(int index)
    {
        lock (Gate)
        {
            var page = Page(index);
            try { return new(Native.FPDF_GetPageWidth(page), Native.FPDF_GetPageHeight(page)); }
            finally { Native.FPDF_ClosePage(page); }
        }
    }
    public Size[] Sizes()
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(document == IntPtr.Zero, this);
            var sizes = new Size[Count];
            for (var i = 0; i < Count; i++)
            {
                if (Native.FPDF_GetPageSizeByIndex(document, i, out var width, out var height) == 0 || width <= 0 || height <= 0)
                    throw new InvalidDataException($"Cannot read dimensions for page {i + 1}.");
                sizes[i] = new Size(width, height);
            }
            return sizes;
        }
    }
    public BitmapSource Render(int index, int width)
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(document == IntPtr.Zero, this);
            width = Math.Clamp(width, 100, 3000);
            var key = (index, width);
            if (cache.TryGetValue(key, out var found)) return found;
            var page = Page(index);
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                var height = Math.Clamp((int)Math.Round(width * Native.FPDF_GetPageHeight(page) / Native.FPDF_GetPageWidth(page)), 1, 6000);
                bitmap = Native.FPDFBitmap_Create(width, height, 0);
                if (bitmap == IntPtr.Zero) throw new OutOfMemoryException("Unable to allocate a page image.");
                Native.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xffffffff);
                Native.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, 1);
                foreach (var bounds in LinkBounds(index, width, height))
                {
                    var left = Math.Clamp((int)bounds.Left, 0, width); var top = Math.Clamp((int)bounds.Top, 0, height);
                    var right = Math.Clamp((int)bounds.Right, 0, width); var bottom = Math.Clamp((int)bounds.Bottom, 0, height);
                    if (right <= left || bottom <= top) continue;
                    Native.FPDFBitmap_FillRect(bitmap, left, top, right - left, 1, 0xff2d7dcd);
                    Native.FPDFBitmap_FillRect(bitmap, left, bottom - 1, right - left, 1, 0xff2d7dcd);
                    Native.FPDFBitmap_FillRect(bitmap, left, top, 1, bottom - top, 0xff2d7dcd);
                    Native.FPDFBitmap_FillRect(bitmap, right - 1, top, 1, bottom - top, 0xff2d7dcd);
                }
                var stride = Native.FPDFBitmap_GetStride(bitmap);
                BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null,
                    Native.FPDFBitmap_GetBuffer(bitmap), stride * height, stride);
                image.Freeze();
                while (order.Count > 0 && cacheBytes + stride * (long)height > 64 * 1024 * 1024)
                {
                    var old = order.Dequeue();
                    var removed = cache[old];
                    cacheBytes -= removed.PixelWidth * (long)removed.PixelHeight * 4;
                    cache.Remove(old);
                }
                cache[key] = image;
                order.Enqueue(key);
                cacheBytes += width * (long)height * 4;
                return image;
            }
            finally
            {
                if (bitmap != IntPtr.Zero) Native.FPDFBitmap_Destroy(bitmap);
                Native.FPDF_ClosePage(page);
            }
        }
    }
    public string Text(int index)
    {
        lock (Gate)
        {
            var page = Page(index);
            var text = Native.FPDFText_LoadPage(page);
            try
            {
                if (text == IntPtr.Zero) return "";
                var count = Native.FPDFText_CountChars(text);
                var buffer = new ushort[count + 1];
                var length = Native.FPDFText_GetText(text, 0, count, buffer);
                return new string(buffer.Take(Math.Max(0, length - 1)).Select(c => (char)c).ToArray());
            }
            finally { if (text != IntPtr.Zero) Native.FPDFText_ClosePage(text); Native.FPDF_ClosePage(page); }
        }
    }
    public (int Page, string? Url)? Link(int index, Point pixel, int width, int height)
    {
        lock (Gate)
        {
            var page = Page(index);
            try
            {
                Native.FPDF_DeviceToPage(page, 0, 0, width, height, 0, (int)pixel.X, (int)pixel.Y, out var x, out var y);
                var link = Native.FPDFLink_GetLinkAtPoint(page, x, y);
                if (link == IntPtr.Zero) return null;
                var dest = Native.FPDFLink_GetDest(document, link);
                var action = Native.FPDFLink_GetAction(link);
                if (dest == IntPtr.Zero && action != IntPtr.Zero) dest = Native.FPDFAction_GetDest(document, action);
                if (dest != IntPtr.Zero) return (Native.FPDFDest_GetDestPageIndex(document, dest), null);
                if (action == IntPtr.Zero) return null;
                var length = Native.FPDFAction_GetURIPath(document, action, null, 0);
                if (length == 0 || length > 32768) return null;
                var buffer = new byte[length];
                Native.FPDFAction_GetURIPath(document, action, buffer, length);
                return (-1, Encoding.UTF8.GetString(buffer).TrimEnd('\0'));
            }
            finally { Native.FPDF_ClosePage(page); }
        }
    }
    public List<Rect> LinkBounds(int index, int width, int height)
    {
        lock (Gate)
        {
            var page = Page(index);
            try
            {
                var result = new List<Rect>(); var position = 0;
                while (Native.FPDFLink_Enumerate(page, ref position, out var link) != 0)
                {
                    if (Native.FPDFLink_GetAnnotRect(link, out var rect) == 0) continue;
                    Native.FPDF_PageToDevice(page, 0, 0, width, height, 0, rect.Left, rect.Top, out var x1, out var y1);
                    Native.FPDF_PageToDevice(page, 0, 0, width, height, 0, rect.Right, rect.Bottom, out var x2, out var y2);
                    result.Add(new Rect(new Point(x1, y1), new Point(x2, y2)));
                }
                return result;
            }
            finally { Native.FPDF_ClosePage(page); }
        }
    }
    public Point ConvertPoint(int index, Point point, int width, int height, bool toPdf)
    {
        lock (Gate)
        {
            var page = Page(index);
            try
            {
                if (toPdf)
                {
                    Native.FPDF_DeviceToPage(page, 0, 0, width, height, 0, (int)point.X, (int)point.Y, out var x, out var y);
                    return new(x, y);
                }
                Native.FPDF_PageToDevice(page, 0, 0, width, height, 0, point.X, point.Y, out var dx, out var dy);
                return new(dx, dy);
            }
            finally { Native.FPDF_ClosePage(page); }
        }
    }
    public void Dispose()
    {
        lock (Gate)
        {
            if (document != IntPtr.Zero) { Native.FPDF_CloseDocument(document); document = IntPtr.Zero; }
            if (memory.IsAllocated) memory.Free();
            cache.Clear(); order.Clear(); cacheBytes = 0;
        }
    }
}

internal static class Native
{
    private const string Dll = "pdfium.dll";
    private static bool initialized;
    public static void Init() { if (!initialized) { FPDF_InitLibrary(); initialized = true; } }
    [DllImport(Dll)] private static extern void FPDF_InitLibrary();
    [DllImport(Dll)] internal static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr size, string? password);
    [DllImport(Dll)] internal static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport(Dll)] internal static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport(Dll)] internal static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [DllImport(Dll)] internal static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Dll)] internal static extern double FPDF_GetPageWidth(IntPtr page);
    [DllImport(Dll)] internal static extern double FPDF_GetPageHeight(IntPtr page);
    [DllImport(Dll)] internal static extern int FPDF_GetPageSizeByIndex(IntPtr doc, int index, out double width, out double height);
    [DllImport(Dll)] internal static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Dll)] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Dll)] internal static extern void FPDFBitmap_FillRect(IntPtr bitmap, int x, int y, int w, int h, uint color);
    [DllImport(Dll)] internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int w, int h, int rotation, int flags);
    [DllImport(Dll)] internal static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Dll)] internal static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Dll)] internal static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Dll)] internal static extern int FPDFText_CountChars(IntPtr text);
    [DllImport(Dll)] internal static extern int FPDFText_GetText(IntPtr text, int start, int count, [Out] ushort[] buffer);
    [DllImport(Dll)] internal static extern void FPDFText_ClosePage(IntPtr text);
    [DllImport(Dll)] internal static extern int FPDF_DeviceToPage(IntPtr page, int x, int y, int width, int height, int rotate, int dx, int dy, out double px, out double py);
    [DllImport(Dll)] internal static extern int FPDF_PageToDevice(IntPtr page, int x, int y, int width, int height, int rotate, double px, double py, out int dx, out int dy);
    [DllImport(Dll)] internal static extern IntPtr FPDFLink_GetLinkAtPoint(IntPtr page, double x, double y);
    [StructLayout(LayoutKind.Sequential)] internal struct LinkRect { public float Left, Top, Right, Bottom; }
    [DllImport(Dll)] internal static extern int FPDFLink_Enumerate(IntPtr page, ref int position, out IntPtr link);
    [DllImport(Dll)] internal static extern int FPDFLink_GetAnnotRect(IntPtr link, out LinkRect rect);
    [DllImport(Dll)] internal static extern IntPtr FPDFLink_GetDest(IntPtr doc, IntPtr link);
    [DllImport(Dll)] internal static extern IntPtr FPDFLink_GetAction(IntPtr link);
    [DllImport(Dll)] internal static extern IntPtr FPDFAction_GetDest(IntPtr doc, IntPtr action);
    [DllImport(Dll)] internal static extern int FPDFDest_GetDestPageIndex(IntPtr doc, IntPtr dest);
    [DllImport(Dll)] internal static extern uint FPDFAction_GetURIPath(IntPtr doc, IntPtr action, [Out] byte[]? buffer, uint length);
}
