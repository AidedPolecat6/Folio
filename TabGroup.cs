using System.Windows;
using System.Windows.Controls;

namespace Folio;

internal sealed class TabGroup : Grid
{
    public List<DocumentTab> Tabs { get; } = new();
    public DocumentTab? Active { get; set; }
    private readonly Dictionary<DocumentTab, double> weights = new();
    public double Weight(DocumentTab tab)
    {
        var index = Tabs.IndexOf(tab);
        return index >= 0 && index * 2 < ColumnDefinitions.Count && ColumnDefinitions[index * 2].ActualWidth > 0
            ? ColumnDefinitions[index * 2].ActualWidth : weights.GetValueOrDefault(tab, 1);
    }
    private void CaptureWidths() { foreach (var tab in Tabs) weights[tab] = Weight(tab); }
    public void Add(DocumentTab tab, int index = -1, double weight = 1)
    {
        CaptureWidths();
        // A newly grouped document gets an equal share of the existing workspace.
        weights[tab] = Tabs.Count > 0 ? weights.Values.Average() : weight;
        if (index < 0 || index > Tabs.Count) Tabs.Add(tab); else Tabs.Insert(index, tab);
        Active = tab; Rebuild();
    }
    public void Remove(DocumentTab tab)
    {
        CaptureWidths(); Tabs.Remove(tab); weights.Remove(tab);
        if (Active == tab) Active = Tabs.FirstOrDefault(); Rebuild();
    }
    public void SetWeights(IReadOnlyList<double> values)
    {
        for (var i = 0; i < Tabs.Count && i < values.Count; i++) weights[Tabs[i]] = double.IsFinite(values[i]) && values[i] > 0 ? values[i] : 1;
        Rebuild();
    }
    private void Rebuild()
    {
        Children.Clear(); ColumnDefinitions.Clear();
        for (var i = 0; i < Tabs.Count; i++)
        {
            if (i > 0)
            {
                ColumnDefinitions.Add(new() { Width = new GridLength(7) });
                var splitter = new GridSplitter { Width = 7, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns };
                SetColumn(splitter, i * 2 - 1); Children.Add(splitter);
            }
            ColumnDefinitions.Add(new() { Width = new GridLength(weights.GetValueOrDefault(Tabs[i], 1), GridUnitType.Star), MinWidth = 150 });
            SetColumn(Tabs[i], i * 2); Children.Add(Tabs[i]);
        }
    }
}
