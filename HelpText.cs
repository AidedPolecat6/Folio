namespace Folio;

internal static class HelpText
{
    public const string Content = """
        WELCOME TO FOLIO
        Open a PDF with Ctrl+O, drag it into Folio, or choose a recent document on Home. Hover over a tool to see what it does.

        YOUR WORKSPACE
        Drop a tab onto the middle of another tab to read them side by side. Drag the divider to resize. Drop a tab into empty tab-strip space to ungroup, or use its right-click menu. Click a PDF to make it active: the blue border and bookmark heading identify it.

        TWO VIEWS OF ONE PDF
        Ctrl+Shift+S splits the active PDF top/bottom. The views scroll and zoom independently; edits are shared. Press the shortcut again to close the lower view.

        READING & SCROLLING
        Settings offers continuous scrolling or one page at a time. Scroll speed is separate; start at 50%. Ctrl+wheel zooms. Width fits the page horizontally; Page fits a whole page. Ctrl+G jumps to a page; Ctrl+F finds the next page containing text. Scanned PDFs need OCR before text search works.

        BOOKMARKS
        The left panel follows the active PDF. Ctrl+B adds a bookmark to its current page. Type a name, then Enter to confirm or Esc to cancel. Right-click bookmarks to rename, delete, nest or reorder. Bookmark edits are saved inside the PDF.

        RECTANGLES
        Choose Rectangle and drag on the page. Folio returns to selection mode automatically. Click a box to show eight resize handles, drag inside to move it, or change its color and thickness. Delete removes the selected box. Arrow keys move a focused resize handle; Shift gives larger steps.

        SAVING
        Ctrl+S saves; File → Save As makes a separate copy. A dot beside the tab title means unsaved changes. Ctrl+Z / Ctrl+Y undo and redo. Folio asks before closing modified PDFs. Editing a signed PDF invalidates its signature.

        HOME
        Pin favorite PDFs with the star. Removing a recent item never deletes the PDF. Folder shortcuts open File Explorer; PDF folder panels show PDFs directly in that folder. Restore last workspace brings back your previous tabs and splits.

        SHORTCUTS
        Ctrl+O             Open PDFs
        Ctrl+Tab          Next PDF in a group, or next standalone workspace
        Ctrl+Shift+Tab   Previous PDF/workspace
        Ctrl+W             Close active tab
        Ctrl+Shift+T      Reopen closed tab
        Ctrl+B              New bookmark
        Ctrl+Shift+S      Toggle top/bottom split
        Ctrl+S              Save
        Ctrl+Z / Ctrl+Y  Undo / redo
        Ctrl+F / Ctrl+G  Search / jump to page
        Alt+Left/Right  Navigation history
        F1                     This guide
        """;
}
