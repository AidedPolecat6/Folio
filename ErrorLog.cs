using System.IO;

namespace Folio;

internal static class ErrorLog
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Folio", "Logs");
    private static readonly object Gate = new();
    internal static void Write(Exception error, string context, string? path = null)
    {
        try
        {
            lock (Gate)
            {
                path ??= Path.Combine(Folder, "errors.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length >= 1024 * 1024) File.Move(path, path + ".old", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} | Folio {typeof(ErrorLog).Assembly.GetName().Version} | {context}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception) { /* Logging must not replace the original error if disk access fails. */ }
    }
}
