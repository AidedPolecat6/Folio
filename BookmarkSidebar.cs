using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfSharp.Pdf;

namespace Folio;

internal sealed class BookmarkSidebar : DockPanel, IDisposable
{
    private DocumentTab? document;
    private PdfFile File => document!.File;
    private PageView Active => document!.Active;
    private readonly TreeView bookmarks = new();
    private readonly TextBlock heading = new() { Margin = new Thickness(14, 0, 14, 12), TextWrapping = TextWrapping.Wrap, Foreground = Ui.Accent };
    public string DocumentName => heading.Text;
    public event Action? HideRequested;
    public BookmarkSidebar()
    {
        Background = new SolidColorBrush(Color.FromRgb(26, 30, 36)); bookmarks.Background = Background;
        var title = new TextBlock { Text = "BOOKMARKS", Margin = new Thickness(14, 16, 14, 7), Foreground = Ui.Muted, FontSize = 11, FontWeight = FontWeights.SemiBold };
        var panelHeader = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(32, 39, 48)) };
        var hide = Ui.Button("‹", "Hide the bookmark panel; use Bookmarks to show it again", () => HideRequested?.Invoke()); DockPanel.SetDock(hide, Dock.Right); panelHeader.Children.Add(hide);
        var labels = new StackPanel(); labels.Children.Add(title); labels.Children.Add(heading); panelHeader.Children.Add(labels);
        var headerBorder = Ui.Band(panelHeader, "#202730"); SetDock(headerBorder, Dock.Top); Children.Add(headerBorder);
        bookmarks.BorderThickness = new Thickness(0, 0, 1, 0); bookmarks.BorderBrush = Ui.Line;
        var actions = new WrapPanel { Margin = new Thickness(8) };
        actions.Children.Add(Ui.Button("+ Bookmark", "Bookmark current page (Ctrl+B)", () => AddBookmark(false)));
        actions.Children.Add(Ui.Button("Rename", "Rename selected bookmark", RenameBookmark));
        SetDock(actions, Dock.Bottom); Children.Add(actions); Children.Add(bookmarks);
        bookmarks.SelectedItemChanged += (_, _) =>
        {
            if (document == null || (bookmarks.SelectedItem as TreeViewItem)?.Tag is not PdfOutline outline) return;
            Ui.Run(() =>
            {
                var page = outline.DestinationPage;
                for (var i = 0; i < File.Document.PageCount; i++) if (File.Document.Pages[i] == page) { Active.Go(i); break; }
            });
        };
        bookmarks.PreviewMouseRightButtonDown += (_, e) =>
        {
            var node = e.OriginalSource as DependencyObject;
            while (node != null && node is not TreeViewItem) node = VisualTreeHelper.GetParent(node);
            if (node is TreeViewItem item) item.IsSelected = true;
        };
        var menu = new ContextMenu();
        menu.Items.Add(Ui.Menu("Add bookmark here…", () => AddBookmark(false)));
        menu.Items.Add(Ui.Menu("Add child bookmark…", () => AddBookmark(true)));
        menu.Items.Add(Ui.Menu("Rename…", RenameBookmark));
        menu.Items.Add(Ui.Menu("Set destination to current page", () => WithSelected((o, _) => o.DestinationPage = File.Document.Pages[Active.Page])));
        menu.Items.Add(Ui.Menu("Move up", () => MoveBookmark(-1)));
        menu.Items.Add(Ui.Menu("Move down", () => MoveBookmark(1)));
        menu.Items.Add(Ui.Menu("Nest under previous bookmark", () => WithSelected((o, list) => { var i = list.IndexOf(o); if (i == 0) return; var parent = list[i - 1]; list.Remove(o); parent.Outlines.Add(o); parent.Opened = true; })));
        menu.Items.Add(Ui.Menu("Move to top level", () => WithSelected((o, list) => { if (list != File.Document.Outlines) { list.Remove(o); File.Document.Outlines.Add(o); } })));
        menu.Items.Add(Ui.Menu("Delete", () => WithSelected((o, list) => list.Remove(o))));
        bookmarks.ContextMenu = menu;
    }
    public void Bind(DocumentTab? tab)
    {
        if (document == tab) return;
        if (document != null) document.File.Changed -= RefreshBookmarks;
        document = tab;
        if (document != null) document.File.Changed += RefreshBookmarks;
        RefreshBookmarks();
    }
    private void RefreshBookmarks()
    {
        bookmarks.Items.Clear(); heading.Text = document == null ? "Select a PDF" : System.IO.Path.GetFileName(File.Path);
        if (document == null) return;
        void Add(ItemsControl parent, PdfOutlineCollection collection)
        {
            foreach (PdfOutline outline in collection)
            {
                var item = new TreeViewItem { Header = outline.Title, Tag = outline, IsExpanded = outline.Opened, ToolTip = outline.Title };
                parent.Items.Add(item); Add(item, outline.Outlines);
            }
        }
        Add(bookmarks, File.Document.Outlines);
    }
    private static PdfOutlineCollection? FindParent(PdfOutline target, PdfOutlineCollection collection)
    {
        foreach (PdfOutline outline in collection)
        {
            if (outline == target) return collection;
            var found = FindParent(target, outline.Outlines); if (found != null) return found;
        }
        return null;
    }
    private void WithSelected(Action<PdfOutline, PdfOutlineCollection> action)
    {
        if (document == null || (bookmarks.SelectedItem as TreeViewItem)?.Tag is not PdfOutline outline) return;
        var list = FindParent(outline, File.Document.Outlines);
        if (list != null) File.Edit(_ => action(outline, list));
    }
    public void AddBookmark(bool child)
    {
        if (document == null) return;
        var name = Ui.Prompt("New bookmark", $"Page {Active.Page + 1}");
        if (name == null) return;
        var parent = child ? (bookmarks.SelectedItem as TreeViewItem)?.Tag as PdfOutline : null;
        File.Edit(doc => (parent?.Outlines ?? doc.Outlines).Add(name, doc.Pages[Active.Page], true));
    }
    private void RenameBookmark()
    {
        if (document == null || (bookmarks.SelectedItem as TreeViewItem)?.Tag is not PdfOutline outline) return;
        var name = Ui.Prompt("Rename bookmark", outline.Title);
        if (name != null) File.Edit(_ => outline.Title = name);
    }
    private void MoveBookmark(int offset) => WithSelected((o, list) =>
    {
        var i = list.IndexOf(o); var target = i + offset;
        if (target < 0 || target >= list.Count) return;
        list.Remove(o); list.Insert(target, o);
    });
    public void Dispose() => Bind(null);
}
