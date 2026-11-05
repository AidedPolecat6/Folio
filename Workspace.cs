namespace Folio;

internal sealed record ViewState(int Page, double Zoom, double Scroll);
internal sealed record TabState(string Path, bool Right, ViewState Top, ViewState? Bottom, double SidebarWidth,
    int Group = -1, double Weight = 1, double SplitRatio = 0.5);
internal sealed record Workspace(TabState[] Tabs, double GroupRatio, int LeftIndex, int RightIndex,
    int Version = 1, int ActiveGroup = 0, int ActiveTab = 0, double SidebarWidth = 260, bool SidebarVisible = true);
