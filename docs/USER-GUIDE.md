# Folio user guide

A local Windows PDF workspace built with C#/.NET 8, WPF, PDFium and PDFsharp.

## Start

Use the **Folio** shortcut in the Start menu or on your desktop. The installation wizard defaults to `%LOCALAPPDATA%\Programs\Folio` and lets you choose another folder. Folio starts on Home. Drop PDFs into the window or use **File → Open**. Launching a PDF from File Explorer opens it directly, including when Folio is already running.

## Home and scrolling

- **Recent PDFs:** click a file to open it, use the star to pin/unpin it, or × to remove it from the list without deleting the PDF. Folio remembers the latest 50 unpinned files plus pinned favorites.
- **Folder shortcuts:** add a folder and click its name to open File Explorer. The … menu renames or removes the shortcut.
- **PDF folders:** each card shows only PDF files directly inside its chosen folder, with its own scroll area, filename filter, and Refresh button. Subfolders are not scanned. Click a filename or select it and press Enter to open it. Unavailable folders display an error inside the card.
- **Home** returns to the start page without closing your PDFs. Bookmarks are hidden on Home. **Restore last workspace** explicitly restores the previous tabs, groups and split views; simply opening and closing Home does not erase that saved workspace.
- **Settings → Scrolling:** choose 10–200% speed, default 50%. Choose continuous scrolling (default) or one page at a time. Both settings persist and also apply to new split views. Ctrl+wheel still zooms. In single-page mode, pause briefly (220 ms) at a page boundary before scrolling again to turn the page. In continuous mode, pages flow without stopping and only nearby pages are rendered.

Preferences, recent files and folder cards are stored locally in `%LOCALAPPDATA%\Folio\library.json`.

## Workflow

| Shortcut | Action |
| --- | --- |
| Ctrl+O | Open PDFs |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous PDF in the active group; on a standalone tab, cycle workspaces |
| Ctrl+W / Ctrl+Shift+T | Close / reopen a tab |
| Ctrl+B | Bookmark the current page; type a name, Enter to accept, Esc to cancel |
| Ctrl+Shift+S | Toggle independent top/bottom views of the active PDF |
| Ctrl+S | Save; **Save As is in the File menu** |
| Ctrl+Z / Ctrl+Y | Undo / redo document changes |
| Ctrl+F | Find the next page containing text |
| Ctrl+G | Go to a page |
| Alt+Left / Alt+Right | Back / forward through page navigation |
| Page Up / Page Down | Previous / next PDF page |
| Ctrl+mouse wheel | Zoom |

**Tab groups:** drop a tab onto the middle of another tab to group their PDFs side by side. Groups have one rounded container with separators between their tabs; other workspaces remain alongside them in the tab strip. A third PDF adds another column. Drag the vertical dividers to resize. Drop onto a tab's edge to reorder (within a group) or place a standalone tab before/after another workspace. Drop into empty tab-strip space, or right-click → **Ungroup this tab**, to detach. The context menu also offers keyboard-accessible **Group side by side with**. **View → Show another view side by side** duplicates the current PDF's view. Clicking a group's tab restores its entire layout.

**Top/bottom split:** each view has independent page position, zoom and scroll. A horizontal divider resizes the panes. Bookmark and annotation changes are shared. The blue border identifies the active view. Ctrl+B and navigation shortcuts apply to that view.

**Bookmarks:** one resizable sidebar on the left follows the PDF you click, including independent top/bottom views. Its heading identifies the active PDF. Right-click a bookmark to rename, delete, move up/down, nest under the previous bookmark, add a child, or update its destination. Changes are saved inside the PDF. The Bookmarks button hides/shows the sidebar.

**Rectangles:** enable **Rectangle**, then drag on the page. Folio automatically returns to selection mode and shows eight corner/edge handles. Click an existing rectangle to select it, drag a handle to resize, or drag inside to move it. The contextual toolbar changes color/thickness or deletes the selected box. Focus a handle and use arrow keys for precision (Shift for larger steps). Changes support undo/redo and persist as standard PDF square annotations after reopening.

**Appearance:** charcoal window chrome, rounded tab groups, flat controls, dark scrollbars, a blue active-view border, and unchanged white PDF pages.

**Links:** click PDF links with Rectangle mode off. Internal links navigate within the document; HTTP/HTTPS/mail links open in the default application. Executable/file-launch PDF actions are not run.

The workspace remembers tabs, groups, split views, pages, zoom, scroll offsets and sidebar widths in `%LOCALAPPDATA%\Folio\workspace.json`. PDFs remain in their original locations. Closing modified documents prompts to save. Writes use a temporary file and atomic replacement, and saving refuses to silently overwrite changes made by another program.

## Current boundaries

- This first release edits bookmarks and rectangle annotations; existing text/image editing, page deletion/reordering, highlights, sticky notes and freehand tools are not implemented.
- Text search finds matching pages, without highlighting individual matches or OCR. Rendered text is not yet selectable.
- Password-protected PDFs are not supported. Editing digitally signed PDFs does not preserve signature validity.
- Undo retains up to 20 full-document snapshots. Large editing workloads use more memory; rendering caches are bounded to roughly 64 MB per document, plus displayed images.
- Session restoration stores positions, not unsaved edits; no crash recovery yet. Optional synchronized scrolling is not implemented.
- Initial page rendering can take longer than cached tab switching. Rendering is serialized because PDFium is not thread-safe.

## Build and checks

Requires the .NET 8 SDK on Windows x64.

```powershell
dotnet build -c Release
& .\bin\Release\net8.0-windows\win-x64\Folio.exe --self-test
Get-Content "$env:TEMP\opencode\folio-selftest.txt"
dotnet publish -c Release --self-contained false -o app
```

Self-tests use temporary synthetic PDFs, never user originals. They cover rendering/cache reuse, internal/web links, bookmark hierarchy, undo/redo, rectangle rendering and persistence, save/reopen, external-write protection, independent split views, and rotated page coordinates.

PDFium binaries and PDFsharp are pinned in `Folio.csproj`. Update dependencies regularly. See `THIRD-PARTY-NOTICES.md` and `licenses/` for upstream licenses.
