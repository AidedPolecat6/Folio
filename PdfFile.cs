using System.IO;
using System.Security.Cryptography;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Folio;

internal sealed class PdfFile : IDisposable
{
    public string Path { get; private set; }
    public PdfDocument Document { get; private set; }
    public PdfEngine Engine { get; private set; }
    private readonly Stack<byte[]> undo = new(), redo = new();
    private byte[] current;
    private byte[] savedHash;
    private byte[] currentHash;
    private byte[] diskHash;
    public bool Dirty => !currentHash.SequenceEqual(savedHash);
    public event Action? Changed;
    public PdfFile(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        current = File.ReadAllBytes(Path);
        diskHash = savedHash = currentHash = SHA256.HashData(current);
        Document = Read(current);
        try { Engine = new(current); }
        catch { Document.Dispose(); throw; }
    }
    private static PdfDocument Read(byte[] bytes)
    {
        var doc = PdfReader.Open(new MemoryStream(bytes, false), PdfDocumentOpenMode.Modify);
        if (doc.PageCount == 0) { doc.Dispose(); throw new InvalidDataException("The PDF has no pages."); }
        return doc;
    }
    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        Document.Save(stream, false);
        return stream.ToArray();
    }
    public void Edit(Action<PdfDocument> change)
    {
        var before = current;
        try
        {
            change(Document);
            var after = Serialize();
            // PDFsharp makes a document immutable after Save; reopen before the next edit.
            var editable = Read(after);
            PdfEngine engine;
            try { engine = new PdfEngine(after); } catch { editable.Dispose(); throw; }
            Document.Dispose(); Document = editable;
            Engine.Dispose(); Engine = engine;
            undo.Push(before); redo.Clear(); current = after; currentHash = SHA256.HashData(current);
            // shortcut: retain at most 20 full-document undo states; use operation-based history for very large editing workloads.
            if (undo.Count > 20) { var keep = undo.Take(20).Reverse().ToArray(); undo.Clear(); foreach (var b in keep) undo.Push(b); }
        }
        catch { Document.Dispose(); Document = Read(before); throw; }
        Changed?.Invoke();
    }
    public void Undo(bool forward = false)
    {
        var source = forward ? redo : undo;
        var target = forward ? undo : redo;
        if (source.Count == 0) return;
        var bytes = source.Peek();
        var doc = Read(bytes);
        PdfEngine engine;
        try { engine = new(bytes); } catch { doc.Dispose(); throw; }
        target.Push(current); source.Pop(); current = bytes; currentHash = SHA256.HashData(current);
        Document.Dispose(); Engine.Dispose(); Document = doc; Engine = engine;
        Changed?.Invoke();
    }
    public void Save(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (string.Equals(path, Path, StringComparison.OrdinalIgnoreCase) && File.Exists(path) &&
            !SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(diskHash))
            throw new IOException("This PDF was changed by another program. Use Save As to keep both versions.");
        var temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, $".folio-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(current); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
            Path = path; diskHash = savedHash = currentHash;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Changed?.Invoke();
    }
    public void Dispose() { Engine.Dispose(); Document.Dispose(); }
}
