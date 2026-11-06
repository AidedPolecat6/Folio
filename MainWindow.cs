using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Folio;

internal sealed class MainWindow : Window
{
    private readonly List<TabGroup> groups = new();
    private readonly List<PdfFile> files = new();
    private readonly Stack<(string Path, int Page)> closed = new();
    private readonly StackPanel tabStrip = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 3, 8, 8) };
    private readonly ContentControl content = new();
    private readonly Grid body = new();
    private readonly BookmarkSidebar bookmarks = new();
    private readonly GridSplitter sidebarDivider = new() { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns };
    private readonly Dictionary<DocumentTab, Border> headers = new();
    private readonly Dictionary<TabGroup, Border> groupHeaders = new();
    private readonly TextBlock dropHint = new() { Text = "Drag a tab onto another to read side by side", Foreground = Ui.Muted, Margin = new Thickness(14, 5, 14, 6), FontSize = 11 };
    private TabGroup? activeGroup;
    internal DocumentTab? Current => homeVisible ? null : activeGroup?.Active;
    private double sidebarWidth = 260;
    private Point tabDown;
    private DocumentTab? dragging;
    private bool dragInProgress;
    private readonly string workspacePath;
    private readonly List<UIElement> documentCommands = new();
    private Button? homeButton;
    private readonly UserLibrary library;
    private readonly HomePage home;
    private bool homeVisible = true, sidebarWanted = true, workspaceTouched, workspaceRestored;
    internal bool IsHomeVisible => homeVisible;
    public MainWindow(string? workspacePath = null)
    {
        this.workspacePath = workspacePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Folio", "workspace.json");
        library = new UserLibrary(Path.Combine(Path.GetDirectoryName(this.workspacePath)!, "library.json"));
        bookmarks.TextSize = library.Data.BookmarkTextSize;
        bookmarks.TextSizeChanged += size => { library.Data.BookmarkTextSize = size; library.Save(); };
        home = new HomePage(library, path => Open(path), RestoreWorkspace);
        Title = "Folio"; Width = 1400; Height = 900; MinWidth = 800; MinHeight = 480;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Folio.ico"));
        Background = new SolidColorBrush(Color.FromRgb(32, 34, 38)); Foreground = new SolidColorBrush(Color.FromRgb(230, 232, 237));
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        SourceInitialized += (_, _) => { var on = 1; var handle = new WindowInteropHelper(this).Handle; DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)); HwndSource.FromHwnd(handle)?.AddHook(HorizontalWheel); };
        var root = new DockPanel { Background = Background }; Content = root;
        var menu = new Menu { Background = (Brush)new BrushConverter().ConvertFromString("#191B1F")!, Padding = new Thickness(8, 1, 8, 1) };
        var file = new MenuItem { Header = "_File" };
        file.Items.Add(Ui.Menu("_Open…    Ctrl+O", OpenDialog));
        file.Items.Add(Ui.Menu("_Save    Ctrl+S", () => Save(false)));
        file.Items.Add(Ui.Menu("Save _As…", () => Save(true)));
        file.Items.Add(new Separator());
        file.Items.Add(Ui.Menu("Close tab    Ctrl+W", () => CloseTab(Current)));
        file.Items.Add(Ui.Menu("Reopen closed tab    Ctrl+Shift+T", Reopen));
        file.Items.Add(Ui.Menu("Exit", Close)); menu.Items.Add(file);
        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(Ui.Menu("Undo    Ctrl+Z", () => Current?.File.Undo()));
        edit.Items.Add(Ui.Menu("Redo    Ctrl+Y", () => Current?.File.Undo(true)));
        edit.Items.Add(Ui.Menu("New bookmark    Ctrl+B", () => bookmarks.AddBookmark(false)));
        edit.Items.Add(Ui.Menu("Find text    Ctrl+F", () => Current?.Active.Search()));
        edit.Items.Add(Ui.Menu("Go to page    Ctrl+G", () => Current?.Active.Jump())); menu.Items.Add(edit);
        var view = new MenuItem { Header = "_View" };
        view.Items.Add(Ui.Menu("Split PDF top/bottom    Ctrl+Shift+S", () => Current?.ToggleSplit()));
        view.Items.Add(Ui.Menu("Ungroup active tab", () => { if (Current != null) Detach(Current); }));
        view.Items.Add(Ui.Menu("Show another view side by side", DuplicateCurrent));
        view.Items.Add(Ui.Menu("Toggle bookmarks sidebar", ToggleBookmarks)); menu.Items.Add(view);
        menu.Items.Add(Ui.Menu("_Settings", ShowSettings));
        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(Ui.Menu("Open error log folder", () => { Directory.CreateDirectory(ErrorLog.Folder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ErrorLog.Folder) { UseShellExecute = true }); }));
        help.Items.Add(Ui.Menu("Getting started & keyboard shortcuts    F1", ShowHelp));
        help.Items.Add(Ui.Menu("About Folio", () => MessageBox.Show("Folio — a focused PDF workspace\n\nDrop a tab onto another to group them side by side.\nDrop on an edge to reorder; drop in the empty tab strip to ungroup.\nCtrl+Tab cycles documents in a group, or workspaces for a single tab.\nCtrl+Shift+S toggles a top/bottom view of the active PDF.\n\nPDFium and PDFsharp. See the included licenses.", "Folio"))); menu.Items.Add(help);
        var menuBand = Ui.Band(menu, "#191B1F"); DockPanel.SetDock(menuBand, Dock.Top); root.Children.Add(menuBand);
        var commands = new WrapPanel { Margin = new Thickness(8, 4, 8, 8) };
        commands.Children.Add(new TextBlock { Text = "FOLIO", Foreground = Ui.Accent, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center });
        homeButton = Ui.Button("Home", "Return to recent PDFs and folder shortcuts", ShowHome); commands.Children.Add(homeButton);
        var openButton = Ui.Button("Open PDF", "Open one or more PDF files · Ctrl+O", OpenDialog); openButton.Background = new SolidColorBrush(Color.FromRgb(42, 94, 141)); commands.Children.Add(openButton);
        documentCommands.Add(Ui.Divider());
        documentCommands.Add(Ui.Button("Save", "Save changes to the active PDF · Ctrl+S", () => Save(false)));
        documentCommands.Add(Ui.Button("+ Bookmark", "Bookmark the current page · Ctrl+B", () => bookmarks.AddBookmark(false)));
        documentCommands.Add(Ui.Button("Split view", "Two independent top/bottom views of this PDF · Ctrl+Shift+S", () => Current?.ToggleSplit()));
        documentCommands.Add(Ui.Button("Bookmarks", "Show or hide the active PDF's left bookmark panel", ToggleBookmarks));
        foreach (var command in documentCommands) commands.Children.Add(command);
        var commandBand = Ui.Band(commands, "#2B2F36"); DockPanel.SetDock(commandBand, Dock.Top); root.Children.Add(commandBand);
        var stripScroll = new ScrollViewer { Content = tabStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, AllowDrop = true, Background = new SolidColorBrush(Color.FromRgb(25, 28, 33)), Padding = new Thickness(0, 7, 0, 0) };
        stripScroll.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent("FolioTab")) return;
            ClearDropFeedback(); dropHint.Text = "Release here to make a separate tab"; e.Effects = DragDropEffects.Move; e.Handled = true;
        };
        stripScroll.Drop += (_, e) => { if (e.Data.GetData("FolioTab") is DocumentTab tab) { Detach(tab); e.Handled = true; } };
        DockPanel.SetDock(stripScroll, Dock.Top); root.Children.Add(stripScroll);
        DockPanel.SetDock(dropHint, Dock.Bottom); root.Children.Add(dropHint);
        body.ColumnDefinitions.Add(new() { Width = new GridLength(sidebarWidth), MinWidth = 180 });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(6) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
        Grid.SetColumn(content, 2); body.Children.Add(content); Grid.SetColumn(sidebarDivider, 1); body.Children.Add(sidebarDivider);
        body.Children.Add(bookmarks); root.Children.Add(body);
        bookmarks.HideRequested += ToggleBookmarks;
        AllowDrop = true;
        DragOver += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { foreach (var path in paths) Open(path); e.Handled = true; } };
        PreviewKeyDown += Keys;
        Closing += (_, e) =>
        {
            foreach (var f in files.ToArray()) if (!ConfirmSave(f)) { e.Cancel = true; return; }
            try { SaveWorkspace(); } catch (Exception error) { ErrorLog.Write(error, "Save workspace"); MessageBox.Show(this, "Could not remember the workspace: " + error.Message, "Folio"); }
        };
        Closed += (_, _) => { bookmarks.Dispose(); foreach (var tab in AllTabs()) tab.Dispose(); foreach (var f in files) f.Dispose(); };
        ShowHome();
        if (library.LoadError != null) Loaded += (_, _) => MessageBox.Show(this, library.LoadError, "Folio");
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private IEnumerable<DocumentTab> AllTabs() => groups.SelectMany(g => g.Tabs);
    private TabGroup GroupOf(DocumentTab tab) => groups.First(g => g.Tabs.Contains(tab));
    private void Activate(DocumentTab tab)
    {
        homeVisible = false; SetSidebarVisibility(); dropHint.Visibility = Visibility.Visible;
        var group = GroupOf(tab); group.Active = tab; activeGroup = group;
        if (content.Content != group) content.Content = group;
        bookmarks.Bind(tab);
        foreach (var doc in AllTabs()) doc.SetHighlighted(doc == tab);
        UpdateTitle();
    }
    private void RenderHeaders()
    {
        tabStrip.Children.Clear(); headers.Clear(); groupHeaders.Clear();
        foreach (var group in groups)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var shell = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(3), Margin = new Thickness(3, 0, 3, 0), BorderThickness = new Thickness(1), Child = row };
            groupHeaders[group] = shell; tabStrip.Children.Add(shell);
            foreach (var doc in group.Tabs)
            {
                if (row.Children.Count > 0) row.Children.Add(new Border { Width = 1, Height = 22, Background = new SolidColorBrush(Color.FromRgb(73, 77, 85)), Margin = new Thickness(2, 0, 2, 0) });
                var inner = new StackPanel { Orientation = Orientation.Horizontal };
                var label = new TextBlock { MaxWidth = 195, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 7, 4, 7), VerticalAlignment = VerticalAlignment.Center };
                inner.Children.Add(label);
                var close = Ui.Button("×", "Close this PDF · Ctrl+W", () => CloseTab(doc)); close.Padding = new Thickness(7, 2, 7, 2); inner.Children.Add(close);
                var head = new Border { Child = inner, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2), Margin = new Thickness(3, 0, 3, 0), AllowDrop = true, Focusable = true, Cursor = Cursors.Hand };
                head.MouseEnter += (_, _) => UpdateTitle(); head.MouseLeave += (_, _) => UpdateTitle();
                headers[doc] = head; row.Children.Add(head);
                AutomationProperties.SetName(head, System.IO.Path.GetFileName(doc.File.Path));
                var context = new ContextMenu();
                var groupWith = new MenuItem { Header = "Group side by side with" };
                foreach (var target in AllTabs().Where(t => t != doc))
                    groupWith.Items.Add(Ui.Menu(Path.GetFileName(target.File.Path), () => Merge(doc, target)));
                groupWith.IsEnabled = groupWith.Items.Count > 0; context.Items.Add(groupWith);
                context.Items.Add(Ui.Menu("Ungroup this tab", () => Detach(doc)));
                context.Items.Add(Ui.Menu("Close", () => CloseTab(doc))); head.ContextMenu = context;
                head.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    if (IsButton(e.OriginalSource as DependencyObject)) return;
                    Activate(doc); head.Focus(); tabDown = e.GetPosition(this); dragging = doc;
                };
                head.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Activate(doc); e.Handled = true; } };
                head.PreviewMouseMove += (_, e) =>
                {
                    if (e.LeftButton != MouseButtonState.Pressed || dragging != doc || dragInProgress || (e.GetPosition(this) - tabDown).Length < 10) return;
                    dragging = null; dragInProgress = true;
                    try { DragDrop.DoDragDrop(head, new DataObject("FolioTab", doc), DragDropEffects.Move); }
                    finally { dragInProgress = false; ClearDropFeedback(); UpdateTitle(); }
                };
                head.MouseLeftButtonUp += (_, _) => dragging = null;
                head.DragOver += (_, e) =>
                {
                    if (e.Data.GetData("FolioTab") is not DocumentTab source) return;
                    ClearDropFeedback();
                    var edge = Edge(head, e.GetPosition(head).X);
                    head.BorderBrush = Ui.Accent;
                    head.BorderThickness = edge < 0 ? new Thickness(3, 0, 0, 0) : edge > 0 ? new Thickness(0, 0, 3, 0) : new Thickness(2);
                    dropHint.Text = source == doc ? "Move to another tab to group, or an empty area to ungroup" : edge == 0 ? "Release to group these PDFs side by side" : "Release to reorder tabs";
                    e.Effects = source == doc ? DragDropEffects.None : DragDropEffects.Move; e.Handled = true;
                };
                head.DragLeave += (_, _) => ClearDropFeedback();
                head.Drop += (_, e) =>
                {
                    if (e.Data.GetData("FolioTab") is not DocumentTab source) return;
                    var edge = Edge(head, e.GetPosition(head).X);
                    if (source != doc) { if (edge == 0) Merge(source, doc); else Reorder(source, doc, edge > 0); }
                    e.Handled = true;
                };
            }
        }
        UpdateTitle();
    }
    private static int Edge(FrameworkElement head, double x) => x < head.ActualWidth * 0.2 ? -1 : x > head.ActualWidth * 0.8 ? 1 : 0;
    private IntPtr HorizontalWheel(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x020E) return IntPtr.Zero; // WM_MOUSEHWHEEL is not exposed by WPF's MouseWheel event.
        var packed = lParam.ToInt64();
        var point = PointFromScreen(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
        var node = InputHitTest(point) as DependencyObject;
        while (node != null)
        {
            if (node is PageView view)
            {
                view.ScrollHorizontally((short)((wParam.ToInt64() >> 16) & 0xffff)); handled = true; break;
            }
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return IntPtr.Zero;
    }
    private static bool IsButton(DependencyObject? node)
    {
        while (node != null) { if (node is ButtonBase) return true; node = VisualTreeHelper.GetParent(node); }
        return false;
    }
    private void ClearDropFeedback()
    {
        foreach (var head in headers.Values) head.BorderThickness = new Thickness(2);
        UpdateTitle();
        dropHint.Text = "Drag a tab onto another to read side by side";
    }
    private void RemoveFromGroup(DocumentTab tab)
    {
        var group = GroupOf(tab); group.Remove(tab);
        if (group.Tabs.Count == 0) groups.Remove(group);
    }
    internal void Merge(DocumentTab source, DocumentTab target)
    {
        if (source == target) return;
        var group = GroupOf(target);
        RemoveFromGroup(source); group.Add(source, group.Tabs.IndexOf(target) + 1);
        RenderHeaders(); Activate(source);
    }
    private void Reorder(DocumentTab source, DocumentTab target, bool after)
    {
        var sourceGroup = GroupOf(source); var targetGroup = GroupOf(target);
        if (sourceGroup == targetGroup)
        {
            sourceGroup.Remove(source); sourceGroup.Add(source, sourceGroup.Tabs.IndexOf(target) + (after ? 1 : 0));
        }
        else
        {
            RemoveFromGroup(source);
            var group = new TabGroup(); group.Add(source); groups.Insert(groups.IndexOf(targetGroup) + (after ? 1 : 0), group);
        }
        RenderHeaders(); Activate(source);
    }
    internal void Detach(DocumentTab tab)
    {
        var previous = GroupOf(tab); if (previous.Tabs.Count == 1) { Activate(tab); return; }
        var index = groups.IndexOf(previous); previous.Remove(tab);
        var group = new TabGroup(); group.Add(tab); groups.Insert(index + 1, group);
        RenderHeaders(); Activate(tab);
    }
    public void Open(string path, int page = 0, bool duplicate = false)
    {
        Ui.Run(() =>
        {
            path = Path.GetFullPath(path);
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new IOException("Please choose a PDF file.");
            var file = files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (!duplicate && file != null) { Activate(AllTabs().First(t => t.File == file)); library.Opened(path); return; }
            if (file == null) { file = new PdfFile(path); files.Add(file); file.Changed += UpdateTitle; }
            var doc = new DocumentTab(file, page, library.Data.ScrollSpeed, library.Data.ContinuousScrolling); doc.Activated += Activate;
            var group = new TabGroup(); group.Add(doc); groups.Add(group);
            workspaceTouched = true; RenderHeaders(); Activate(doc); library.Opened(path);
        });
    }
    private void OpenDialog()
    {
        var picker = new OpenFileDialog { Filter = "PDF documents|*.pdf", Multiselect = true };
        if (picker.ShowDialog(this) == true) foreach (var path in picker.FileNames) Open(path);
    }
    private void UpdateTitle()
    {
        foreach (var command in documentCommands) command.Visibility = homeVisible ? Visibility.Collapsed : Visibility.Visible;
        if (homeButton != null) { homeButton.Background = homeVisible ? new SolidColorBrush(Color.FromRgb(62, 79, 101)) : Brushes.Transparent; homeButton.BorderBrush = homeVisible ? Ui.Accent : Brushes.Transparent; }
        foreach (var group in groups)
        foreach (var doc in group.Tabs)
        {
            if (!headers.TryGetValue(doc, out var head)) continue;
            ((TextBlock)((StackPanel)head.Child).Children[0]).Text = Path.GetFileName(doc.File.Path) + (doc.File.Dirty ? " •" : "");
            head.ToolTip = doc.File.Path;
            head.Background = doc == Current ? new SolidColorBrush(Color.FromRgb(52, 77, 102)) : head.IsMouseOver ? new SolidColorBrush(Color.FromRgb(65, 69, 77)) : Ui.Surface;
            head.BorderBrush = group.Tabs.Count > 1 ? Brushes.Transparent : doc == Current || head.IsKeyboardFocused ? Ui.Accent : Ui.Line;
        }
        foreach (var (group, border) in groupHeaders)
        {
            border.Background = new SolidColorBrush(!homeVisible && group == activeGroup ? Color.FromRgb(44, 48, 54) : Color.FromRgb(26, 28, 32));
            border.BorderBrush = group.Tabs.Count > 1 ? (!homeVisible && group == activeGroup ? Ui.Accent : Ui.Line) : Brushes.Transparent;
        }
        Title = Current == null ? "Folio — Home" : $"{Path.GetFileName(Current.File.Path)}{(Current.File.Dirty ? " •" : "")} — Folio";
    }
    internal void ShowHome()
    {
        if (bookmarks.Visibility == Visibility.Visible && body.ColumnDefinitions[0].ActualWidth > 0) sidebarWidth = body.ColumnDefinitions[0].ActualWidth;
        homeVisible = true; content.Content = home; bookmarks.Bind(null); SetSidebarVisibility(); dropHint.Visibility = Visibility.Collapsed;
        home.RefreshRecent(); home.SetRestoreEnabled(!workspaceRestored && File.Exists(workspacePath)); UpdateTitle();
    }
    private void ToggleBookmarks()
    {
        if (homeVisible) return;
        if (sidebarWanted && body.ColumnDefinitions[0].ActualWidth > 0) sidebarWidth = body.ColumnDefinitions[0].ActualWidth;
        sidebarWanted = !sidebarWanted; SetSidebarVisibility();
    }
    private void SetSidebarVisibility(bool rememberWidth = true)
    {
        if (rememberWidth && bookmarks.Visibility == Visibility.Visible && body.ColumnDefinitions[0].ActualWidth > 0) sidebarWidth = body.ColumnDefinitions[0].ActualWidth;
        var visible = sidebarWanted && !homeVisible;
        bookmarks.Visibility = sidebarDivider.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        body.ColumnDefinitions[1].Width = new GridLength(visible ? 6 : 0);
        body.ColumnDefinitions[0].MinWidth = visible ? 180 : 0;
        body.ColumnDefinitions[0].Width = new GridLength(visible ? Math.Max(180, sidebarWidth) : 0);
    }
    private void ShowSettings()
    {
        var original = library.Data.ScrollSpeed;
        var originalMode = library.Data.ContinuousScrolling;
        var panel = new StackPanel { Margin = new Thickness(22), Width = 360 };
        panel.Children.Add(new TextBlock { Text = "Scrolling", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Slower ←                         → Faster", Foreground = Ui.Muted, Margin = new Thickness(0, 18, 0, 8) });
        var value = new TextBlock { Margin = new Thickness(0, 8, 0, 8) };
        var slider = new Slider { Minimum = 10, Maximum = 200, Value = original * 100, TickFrequency = 10, IsSnapToTickEnabled = true, ToolTip = "Adjust mouse-wheel and touchpad scrolling in Folio only. 50% is the default; smaller values move less per gesture." };
        AutomationProperties.SetName(slider, "Scroll speed percent");
        void Apply(double speed)
        {
            library.Data.ScrollSpeed = speed; value.Text = $"{speed:P0} · default 50 %";
            foreach (var doc in AllTabs()) { doc.Top.ScrollSpeed = speed; if (doc.Bottom != null) doc.Bottom.ScrollSpeed = speed; }
        }
        slider.ValueChanged += (_, _) => Apply(slider.Value / 100); Apply(original);
        panel.Children.Add(slider); panel.Children.Add(value);
        panel.Children.Add(new TextBlock { Text = "Applies only to Folio. Ctrl+wheel still zooms.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, Margin = new Thickness(0, 4, 0, 14) });
        panel.Children.Add(new TextBlock { Text = "Page layout", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 8) });
        var continuousOption = new RadioButton { Content = "Continuous scrolling (recommended)", IsChecked = originalMode, ToolTip = "Pages follow each other without stopping. Only nearby pages are rendered to keep large PDFs responsive.", Margin = new Thickness(0, 4, 0, 4) };
        var singleOption = new RadioButton { Content = "One page at a time", IsChecked = !originalMode, ToolTip = "Display a single page. Pause briefly at its edge and scroll again to change pages.", Margin = new Thickness(0, 8, 0, 4) };
        panel.Children.Add(continuousOption);
        panel.Children.Add(new TextBlock { Text = "Read smoothly through pages with a small gap between them.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, Margin = new Thickness(20, 0, 0, 4) });
        panel.Children.Add(singleOption);
        panel.Children.Add(new TextBlock { Text = "Stop at page edges. Scroll again after a brief pause to turn the page.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, Margin = new Thickness(20, 0, 0, 16) });
        void ApplyMode(bool mode)
        {
            library.Data.ContinuousScrolling = mode;
            foreach (var doc in AllTabs()) { doc.Top.Continuous = mode; if (doc.Bottom != null) doc.Bottom.Continuous = mode; }
        }
        continuousOption.Checked += (_, _) => ApplyMode(true); singleOption.Checked += (_, _) => ApplyMode(false);
        var dialog = new Window { Title = "Folio settings", Owner = this, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Ui.Button("Reset", "Restore 50% scrolling and continuous page layout", () => { slider.Value = 50; continuousOption.IsChecked = true; }));
        var save = Ui.Button("Save", "Remember scroll speed and page layout between launches", () => { library.Save(); dialog.DialogResult = true; }); save.IsDefault = true; buttons.Children.Add(save);
        var cancel = Ui.Button("Cancel", "Keep previous settings", () => dialog.DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel); panel.Children.Add(buttons);
        if (dialog.ShowDialog() != true) { Apply(original); ApplyMode(originalMode); }
    }
    private void ShowHelp()
    {
        var text = new TextBlock { Text = HelpText.Content, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24), FontSize = 14 };
        new Window { Title = "Folio · Getting started", Owner = this, Width = 650, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.ShowDialog();
    }
    private bool SaveFile(PdfFile file, bool saveAs)
    {
        var path = file.Path;
        if (saveAs)
        {
            var picker = new SaveFileDialog { Filter = "PDF documents|*.pdf", DefaultExt = ".pdf", FileName = Path.GetFileName(path), InitialDirectory = Path.GetDirectoryName(path), OverwritePrompt = true };
            if (picker.ShowDialog(this) != true) return false; path = picker.FileName;
        }
        if (files.Any(f => f != file && string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) throw new IOException("That PDF is open in another tab. Choose a different name.");
        file.Save(path); library.Opened(path); return true;
    }
    private void Save(bool saveAs) { if (Current != null) SaveFile(Current.File, saveAs); }
    private bool ConfirmSave(PdfFile file)
    {
        if (!file.Dirty) return true;
        var answer = MessageBox.Show(this, $"Save changes to {Path.GetFileName(file.Path)}?", "Folio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No) return true;
        try { return SaveFile(file, false); } catch (Exception e) { ErrorLog.Write(e, "Save PDF before closing"); MessageBox.Show(this, e.Message, "Folio"); return false; }
    }
    internal void CloseTab(DocumentTab? tab)
    {
        if (tab == null) return;
        var f = tab.File; var last = AllTabs().Count(t => t.File == f) == 1;
        if (last && !ConfirmSave(f)) return;
        closed.Push((f.Path, tab.Active.Page));
        var oldGroup = GroupOf(tab); var wasCurrent = Current == tab;
        if (wasCurrent) bookmarks.Bind(null);
        RemoveFromGroup(tab); tab.Dispose();
        if (last) { files.Remove(f); f.Changed -= UpdateTitle; f.Dispose(); }
        if (groups.Count == 0) { activeGroup = null; ShowHome(); }
        else if (wasCurrent) Activate(oldGroup.Active ?? groups[0].Active!);
        RenderHeaders();
    }
    private void Reopen() { if (closed.TryPop(out var tab)) Open(tab.Path, tab.Page, true); }
    private void DuplicateCurrent()
    {
        if (Current == null) return;
        var target = Current; Open(target.File.Path, target.Active.Page, true);
        if (Current != target) Merge(Current!, target);
    }
    internal Workspace CaptureWorkspace()
    {
        static ViewState State(PageView view) => new(view.Page, view.Zoom, view.ReadingOffset);
        var tabs = groups.SelectMany((g, i) => g.Tabs.Select(d => new TabState(d.File.Path, false, State(d.Top), d.Bottom == null ? null : State(d.Bottom), sidebarWidth, i, g.Weight(d), d.SplitRatio))).ToArray();
        var width = bookmarks.Visibility == Visibility.Visible ? body.ColumnDefinitions[0].ActualWidth : sidebarWidth;
        return new Workspace(tabs, 0.5, 0, 0, 2, activeGroup == null ? 0 : groups.IndexOf(activeGroup), activeGroup?.Tabs.IndexOf(activeGroup.Active!) ?? 0, width > 0 ? width : sidebarWidth, sidebarWanted);
    }
    private void SaveWorkspace()
    {
        if (!workspaceTouched && groups.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(workspacePath)!);
        var temp = workspacePath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(CaptureWorkspace())); File.Move(temp, workspacePath, true);
    }
    public void RestoreWorkspace()
    {
        if (workspaceRestored || !File.Exists(workspacePath)) return;
        try
        {
            var state = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(workspacePath)); if (state?.Tabs == null) return;
            var restored = new Dictionary<int, List<(DocumentTab Tab, double Weight)>>();
            var legacyLeft = state.Tabs.Where(t => !t.Right).ElementAtOrDefault(Math.Max(0, state.LeftIndex));
            var legacyRight = state.Tabs.Where(t => t.Right).ElementAtOrDefault(Math.Max(0, state.RightIndex));
            var unique = 1;
            foreach (var saved in state.Tabs.Take(40))
            {
                if (!File.Exists(saved.Path)) continue;
                var before = AllTabs().Count(); Open(saved.Path, saved.Top.Page, true);
                if (AllTabs().Count() == before) continue;
                var doc = Current!; doc.Restore(saved);
                var index = state.Version >= 2 ? saved.Group : saved == legacyLeft || saved == legacyRight ? 0 : unique++;
                if (!restored.TryGetValue(index, out var list)) restored[index] = list = new();
                list.Add((doc, saved.Weight));
            }
            foreach (var list in restored.Values)
            {
                var first = list[0].Tab;
                foreach (var item in list.Skip(1)) { RemoveFromGroup(item.Tab); GroupOf(first).Add(item.Tab); }
                GroupOf(first).SetWeights(list.Select(x => x.Weight).ToArray());
            }
            sidebarWidth = double.IsFinite(state.SidebarWidth) ? Math.Clamp(state.SidebarWidth, 180, 600) : 260;
            sidebarWanted = state.SidebarVisible; SetSidebarVisibility(false);
            RenderHeaders();
            if (groups.Count > 0)
            {
                if (state.Version < 2 && restored.TryGetValue(0, out var legacyActive)) Activate(legacyActive[0].Tab);
                else
                {
                    var restoredGroups = restored.Values.Select(list => GroupOf(list[0].Tab)).ToArray();
                    if (restoredGroups.Length > 0) { var group = restoredGroups[Math.Clamp(state.ActiveGroup, 0, restoredGroups.Length - 1)]; Activate(group.Tabs[Math.Clamp(state.ActiveTab, 0, group.Tabs.Count - 1)]); }
                }
            }
            workspaceRestored = true; home.SetRestoreEnabled(false);
        }
        catch (Exception e) { ErrorLog.Write(e, "Restore workspace"); MessageBox.Show(this, "Could not restore the previous workspace: " + e.Message, "Folio"); }
    }
    private void Keys(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1) { e.Handled = true; ShowHelp(); return; }
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control); var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (control && e.Key == Key.Tab)
        {
            if (activeGroup != null && !homeVisible)
            {
                var delta = shift ? -1 : 1;
                if (activeGroup.Tabs.Count > 1) Activate(activeGroup.Tabs[(activeGroup.Tabs.IndexOf(Current!) + delta + activeGroup.Tabs.Count) % activeGroup.Tabs.Count]);
                else { var group = groups[(groups.IndexOf(activeGroup) + delta + groups.Count) % groups.Count]; Activate(group.Active!); }
            }
            e.Handled = true; return;
        }
        if (Keyboard.FocusedElement is TextBox && e.Key is Key.Z or Key.Y or Key.A or Key.C or Key.V or Key.X) return;
        Action? action = control ? e.Key switch
        {
            Key.O => OpenDialog, Key.S when shift => () => Current?.ToggleSplit(), Key.S => () => Save(false),
            Key.B => () => bookmarks.AddBookmark(false), Key.W => () => CloseTab(Current), Key.T when shift => Reopen,
            Key.F => () => Current?.Active.Search(), Key.G => () => Current?.Active.Jump(),
            Key.Z => () => Current?.File.Undo(), Key.Y => () => Current?.File.Undo(true), _ => null
        } : null;
        if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Left) action = () => Current?.Active.History(false);
        if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Right) action = () => Current?.Active.History(true);
        if (!control && Keyboard.FocusedElement is not TextBox && Current != null)
        {
            if (e.Key == Key.PageDown) action = () => Current.Active.Go(Current.Active.Page + 1);
            if (e.Key == Key.PageUp) action = () => Current.Active.Go(Current.Active.Page - 1);
            if (e.Key == Key.Delete) action = Current.Active.DeleteRectangle;
        }
        if (action != null) { e.Handled = true; Ui.Run(action); }
    }
}
