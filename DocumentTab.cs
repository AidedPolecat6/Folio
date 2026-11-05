using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Folio;

internal sealed class DocumentTab : Grid, IDisposable
{
    public PdfFile File { get; }
    public PageView Active { get; private set; }
    public PageView Top { get; }
    public PageView? Bottom { get; private set; }
    public event Action<DocumentTab>? Activated;
    private readonly Border topBorder, bottomBorder = new();
    private readonly GridSplitter horizontal = new() { Height = 7, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows };
    public DocumentTab(PdfFile file, int page = 0, double scrollSpeed = 0.5, bool continuous = true)
    {
        File = file;
        Top = Active = new PageView(file, page) { ScrollSpeed = scrollSpeed, Continuous = continuous };
        topBorder = new Border { Child = Top, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
        Children.Add(topBorder); Top.Activated += SetActive;
    }
    private void SetActive(PageView view) { Active = view; SetHighlighted(true); Activated?.Invoke(this); }
    public void SetHighlighted(bool value)
    {
        topBorder.BorderBrush = value && Active == Top ? Ui.Accent : Brushes.Transparent;
        bottomBorder.BorderBrush = value && Active == Bottom ? Ui.Accent : Brushes.Transparent;
    }
    public double SplitRatio => Bottom == null || ActualHeight < 10 ? 0.5 : RowDefinitions[0].ActualHeight / Math.Max(1, ActualHeight - 7);
    public void Restore(TabState state)
    {
        void RestoreView(PageView view, ViewState saved)
        {
            view.RestorePosition(saved);
        }
        RestoreView(Top, state.Top);
        if (state.Bottom != null)
        {
            ToggleSplit(); RestoreView(Bottom!, state.Bottom);
            var ratio = double.IsFinite(state.SplitRatio) ? Math.Clamp(state.SplitRatio, 0.1, 0.9) : 0.5;
            RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
            RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
        }
    }
    public void ToggleSplit()
    {
        if (Bottom != null)
        {
            Bottom.Dispose(); Bottom = null; bottomBorder.Child = null;
            Children.Remove(bottomBorder); Children.Remove(horizontal); RowDefinitions.Clear();
            SetActive(Top); return;
        }
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });
        RowDefinitions.Add(new() { Height = new GridLength(7) });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });
        Bottom = new PageView(File, Active.Page) { ScrollSpeed = Top.ScrollSpeed, Continuous = Top.Continuous }; Bottom.Activated += SetActive;
        bottomBorder.Child = Bottom; bottomBorder.BorderThickness = new Thickness(1);
        SetRow(horizontal, 1); SetRow(bottomBorder, 2); Children.Add(horizontal); Children.Add(bottomBorder);
        SetActive(Bottom);
    }
    public void Dispose() { Top.Dispose(); Bottom?.Dispose(); }
}
