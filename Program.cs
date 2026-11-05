using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;

namespace Folio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
        if (args.Length == 2 && args[0] == "--demo-capture") { DemoCapture.Run(args[1]); return; }
        var id = "Folio-" + Environment.UserName;
        using var mutex = new Mutex(true, id, out var first);
        if (!first)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", id, PipeDirection.Out);
                client.Connect(3000);
                using var writer = new StreamWriter(client); writer.WriteLine(JsonSerializer.Serialize(args.Select(Path.GetFullPath).ToArray()));
            }
            catch (Exception e) { MessageBox.Show(e.Message, "Folio"); }
            return;
        }
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
        var window = new MainWindow();
        app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(e.Exception.Message, "Folio"); e.Handled = true; };
        window.Loaded += (_, _) => { foreach (var path in args) window.Open(path); };
        _ = Task.Run(async () =>
        {
            while (true)
            {
                using var pipe = new NamedPipeServerStream(id, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                try
                {
                    using var reader = new StreamReader(pipe);
                    var line = await reader.ReadLineAsync();
                    var paths = JsonSerializer.Deserialize<string[]>(line ?? "[]") ?? [];
                    await app.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var path in paths) window.Open(path);
                        if (paths.Length == 0) window.ShowHome();
                        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                        window.Activate();
                    });
                }
                catch (Exception e) when (e is IOException or JsonException) { }
            }
        });
        app.Run(window);
    }
}
