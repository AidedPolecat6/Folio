using System.IO;
using System.Text.Json;

namespace Folio;

internal sealed record RecentPdf(string Path, DateTimeOffset Opened, bool Pinned = false);
internal sealed record SavedFolder(string Path, string Name);
internal sealed class LibraryData
{
    public double ScrollSpeed { get; set; } = 0.5;
    public bool ContinuousScrolling { get; set; } = true;
    public double BookmarkTextSize { get; set; } = 12;
    public List<RecentPdf> Recent { get; set; } = new();
    public List<SavedFolder> Shortcuts { get; set; } = new();
    public List<SavedFolder> PdfFolders { get; set; } = new();
}

internal sealed class UserLibrary
{
    private readonly string path;
    public LibraryData Data { get; }
    public string? LoadError { get; }
    public UserLibrary(string path)
    {
        this.path = path;
        try { Data = File.Exists(path) ? JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { Data = new(); LoadError = $"Could not read Folio's settings: {e.Message}\nThe existing file has been preserved: {path}"; }
        Data.ScrollSpeed = double.IsFinite(Data.ScrollSpeed) ? Math.Clamp(Data.ScrollSpeed, 0.1, 2) : 0.5;
        Data.BookmarkTextSize = double.IsFinite(Data.BookmarkTextSize) ? Math.Clamp(Data.BookmarkTextSize, 10, 24) : 12;
        Data.Recent = (Data.Recent ?? new()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Path)).ToList();
        Data.Shortcuts = (Data.Shortcuts ?? new()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Path)).ToList();
        Data.PdfFolders = (Data.PdfFolders ?? new()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Path)).ToList();
    }
    public void Save()
    {
        if (LoadError != null) throw new IOException(LoadError);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(Data)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Opened(string file)
    {
        file = System.IO.Path.GetFullPath(file);
        var existing = Data.Recent.FirstOrDefault(x => string.Equals(x.Path, file, StringComparison.OrdinalIgnoreCase));
        Data.Recent.RemoveAll(x => string.Equals(x.Path, file, StringComparison.OrdinalIgnoreCase));
        Data.Recent.Insert(0, new(file, DateTimeOffset.UtcNow, existing?.Pinned ?? false));
        Data.Recent = Data.Recent.Where(x => x.Pinned).Concat(Data.Recent.Where(x => !x.Pinned).Take(50)).ToList();
        Save();
    }
    public static string[] PdfFiles(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
        .Where(p => string.Equals(System.IO.Path.GetExtension(p), ".pdf", StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => System.IO.Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase).ToArray();
}
