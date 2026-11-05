using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Folio;

internal sealed class HomePage : ScrollViewer
{
    private readonly UserLibrary library;
    private readonly Action<string> open;
    private readonly StackPanel recent = new();
    private readonly WrapPanel shortcuts = new(), folders = new();
    private readonly Button restore;
    public HomePage(UserLibrary library, Action<string> open, Action restoreWorkspace)
    {
        this.library = library; this.open = open;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var page = new StackPanel { Margin = new Thickness(28, 20, 28, 24) }; Content = page;
        page.Children.Add(new TextBlock { Text = "Welcome to Folio", FontSize = 25, FontWeight = FontWeights.SemiBold });
        page.Children.Add(new TextBlock { Text = "Your recent PDFs and folders, in one place.", Foreground = Ui.Muted, Margin = new Thickness(0, 7, 0, 16) });
        restore = Ui.Button("Restore last workspace", "Reopen your previous tabs and split views", restoreWorkspace);
        restore.HorizontalAlignment = HorizontalAlignment.Left; page.Children.Add(restore);
        var recentSection = new StackPanel(); recentSection.Children.Add(Heading("Recent PDFs"));
        var recentScroll = new ScrollViewer { Content = recent, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        GentleScroll(recentScroll); recentSection.Children.Add(recentScroll); page.Children.Add(Card(recentSection));
        var shortcutSection = new StackPanel(); shortcutSection.Children.Add(Section("Folder shortcuts", "+ Add shortcut", () => AddFolder(false))); shortcutSection.Children.Add(shortcuts); page.Children.Add(Card(shortcutSection));
        var folderSection = new StackPanel(); folderSection.Children.Add(Section("PDF folders", "+ Add PDF folder", () => AddFolder(true))); folderSection.Children.Add(folders); page.Children.Add(Card(folderSection));
        GentleScroll(this);
        RefreshRecent(); RefreshFolders();
    }
    public void SetRestoreEnabled(bool enabled) => restore.IsEnabled = enabled;
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 10) };
    private static Border Card(UIElement content) => new() { Child = content, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 14, 12, 8) };
    private static DockPanel Section(string title, string button, Action action)
    {
        var row = new DockPanel { Margin = new Thickness(0, 16, 0, 6) };
        var add = Ui.Button(button, button, action); DockPanel.SetDock(add, Dock.Right); row.Children.Add(add);
        row.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
    private void GentleScroll(ScrollViewer scroll)
    {
        scroll.PreviewMouseWheel += (_, e) =>
        {
            // Nested lists own their wheel events; scrolling one panel must not move the whole home page.
            var node = e.OriginalSource as DependencyObject;
            while (node != null && node != scroll)
            {
                if (node is ScrollViewer) return;
                node = VisualTreeHelper.GetParent(node);
            }
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 120.0 * 48 * library.Data.ScrollSpeed); e.Handled = true;
        };
    }
    public void RefreshRecent()
    {
        recent.Children.Clear();
        if (library.Data.Recent.Count == 0) { recent.Children.Add(new TextBlock { Text = "Open a PDF to start your recent list.", Foreground = Ui.Muted, Margin = new Thickness(12) }); return; }
        foreach (var item in library.Data.Recent.OrderByDescending(x => x.Pinned).ThenByDescending(x => x.Opened))
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var remove = Ui.Button("×", "Remove from this list (keeps the PDF file)", () => { library.Data.Recent.Remove(item); library.Save(); RefreshRecent(); });
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var pin = Ui.Button(item.Pinned ? "★" : "☆", item.Pinned ? "Unpin PDF" : "Pin PDF", () =>
            {
                var i = library.Data.Recent.IndexOf(item); if (i < 0) return;
                library.Data.Recent[i] = item with { Pinned = !item.Pinned }; library.Save(); RefreshRecent();
            });
            DockPanel.SetDock(pin, Dock.Right); row.Children.Add(pin);
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = Path.GetFileName(item.Path), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold });
            label.Children.Add(new TextBlock { Text = $"{Path.GetDirectoryName(item.Path)}   ·   {item.Opened.ToLocalTime():g}", Foreground = Ui.Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            var button = Ui.Button("", item.Path, () => open(item.Path)); button.Content = label; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetName(button, "Open " + Path.GetFileName(item.Path)); row.Children.Add(button); recent.Children.Add(row);
        }
    }
    private void AddFolder(bool panel)
    {
        var picker = new OpenFolderDialog { Title = panel ? "Choose a folder of PDFs" : "Choose a folder shortcut" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        var list = panel ? library.Data.PdfFolders : library.Data.Shortcuts;
        if (list.Any(x => string.Equals(x.Path, picker.FolderName, StringComparison.OrdinalIgnoreCase))) return;
        var name = new DirectoryInfo(picker.FolderName).Name;
        list.Add(new(picker.FolderName, string.IsNullOrWhiteSpace(name) ? picker.FolderName : name)); library.Save(); RefreshFolders();
    }
    private void RefreshFolders()
    {
        shortcuts.Children.Clear(); folders.Children.Clear();
        foreach (var folder in library.Data.Shortcuts)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Ui.Button(folder.Name, folder.Path, () =>
            {
                if (!Directory.Exists(folder.Path)) throw new DirectoryNotFoundException("Folder is unavailable: " + folder.Path);
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder.Path }, UseShellExecute = true });
            }));
            row.Children.Add(FolderMenu(folder, false)); shortcuts.Children.Add(Card(row));
        }
        if (shortcuts.Children.Count == 0) shortcuts.Children.Add(Ui.Button("+ Choose a folder shortcut", "Choose a folder to open directly in File Explorer", () => AddFolder(false)));
        foreach (var folder in library.Data.PdfFolders) folders.Children.Add(FolderPanel(folder));
        if (folders.Children.Count == 0) folders.Children.Add(Ui.Button("+ Browse a PDF folder", "Choose a folder to show its PDFs in a scrollable panel", () => AddFolder(true)));
    }
    private Button FolderMenu(SavedFolder folder, bool panel)
    {
        var menu = new ContextMenu(); var list = panel ? library.Data.PdfFolders : library.Data.Shortcuts;
        menu.Items.Add(Ui.Menu("Rename…", () =>
        {
            var name = Ui.Prompt("Folder name", folder.Name); if (name == null) return;
            var index = list.IndexOf(folder); if (index < 0) return;
            list[index] = folder with { Name = name }; library.Save(); RefreshFolders();
        }));
        menu.Items.Add(Ui.Menu("Remove from home", () => { list.Remove(folder); library.Save(); RefreshFolders(); }));
        var button = Ui.Button("…", "Folder options", () => menu.IsOpen = true); menu.PlacementTarget = button; button.ContextMenu = menu; return button;
    }
    private Border FolderPanel(SavedFolder folder)
    {
        var panel = new DockPanel { Width = 390, Height = 345 };
        var title = new DockPanel();
        var menu = FolderMenu(folder, true); DockPanel.SetDock(menu, Dock.Right); title.Children.Add(menu);
        title.Children.Add(new TextBlock { Text = folder.Name, ToolTip = folder.Path, FontWeight = FontWeights.SemiBold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
        var searchRow = new DockPanel { Margin = new Thickness(0, 7, 0, 7) };
        var search = new TextBox { ToolTip = "Filter PDF filenames", Margin = new Thickness(0, 0, 5, 0) };
        AutomationProperties.SetName(search, "Filter " + folder.Name);
        var status = new TextBlock { Foreground = Ui.Muted, FontSize = 11, Margin = new Thickness(0, 4, 0, 2), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        var list = new ListBox { Background = Ui.Surface, BorderThickness = new Thickness(0), DisplayMemberPath = "Name" };
        VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        string[] paths = []; var request = 0; var loaded = false;
        void Filter()
        {
            var filtered = paths.Where(p => Path.GetFileName(p).Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            list.ItemsSource = filtered.Select(p => new SavedFolder(p, Path.GetFileName(p))).ToArray(); status.Text = $"{filtered.Length} PDFs · this folder only";
        }
        Button? refresh = null;
        async void Load()
        {
            var ticket = ++request; status.Text = "Loading PDFs…"; refresh!.IsEnabled = false;
            try
            {
                var found = await Task.Run(() => UserLibrary.PdfFiles(folder.Path));
                if (request != ticket) return; paths = found; loaded = true; Filter();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            { if (request == ticket) { paths = []; list.ItemsSource = null; status.Text = "Cannot read folder: " + e.Message; } }
            finally { if (request == ticket) refresh.IsEnabled = true; }
        }
        refresh = Ui.Button("Refresh", "Reload PDFs in this folder", Load); DockPanel.SetDock(refresh, Dock.Right); searchRow.Children.Add(refresh); searchRow.Children.Add(search);
        DockPanel.SetDock(searchRow, Dock.Top); panel.Children.Add(searchRow); panel.Children.Add(list);
        search.TextChanged += (_, _) => Filter();
        list.PreviewMouseWheel += (_, e) =>
        {
            var scroll = FindScroll(list); if (scroll == null) return;
            // Pixel scrolling retains virtualization and keeps the outer home page stationary.
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 120.0 * 48 * library.Data.ScrollSpeed); e.Handled = true;
        };
        list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem item && item.DataContext is SavedFolder pdf) open(pdf.Path);
        };
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter && list.SelectedItem is SavedFolder pdf) { open(pdf.Path); e.Handled = true; } };
        panel.Loaded += (_, _) => { if (!loaded) Load(); };
        panel.Unloaded += (_, _) => { request++; refresh.IsEnabled = true; };
        return Card(panel);
    }
    private static ScrollViewer? FindScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var found = FindScroll(VisualTreeHelper.GetChild(parent, i)); if (found != null) return found; }
        return null;
    }
}
