# Screenshot capture and ChatGPT attach

Use this file to reimplement the Windows screenshot feature on another platform or after a rewrite. The live code lives in `MainWindow.xaml` and `MainWindow.xaml.cs`.

## Product behavior

- **Hotkey:** `Ctrl+Shift+X` (global).
- **UI:** `Screenshot` button on the caption action row (same row as Copy Latest, Copy All, Paste & Send, Clear).
- **What is captured:** the Windows **primary / main display** only (`Screen.PrimaryScreen.Bounds`). Do not capture the secondary monitor. Meetings run on the main display; PrivateBrowser is kept off that screen or excluded from capture.
- **Where it goes:** the PNG is attached to the ChatGPT composer. An analysis prompt is inserted as draft text. **Do not click Send.**
- **Requirement:** ChatGPT (`chatgpt.com`) must already be open in the WebView.

Draft prompt currently used:

```
Please analyze the attached screenshot. If it contains a coding problem, written question, exam prompt, or other on-screen task, provide a correct and complete solution. If it shows code, identify any issues and include a corrected implementation. Present the answer clearly and precisely.
```

## Why the old heuristic was wrong

The first version captured “the screen that does not host PrivateBrowser.” If PrivateBrowser sat on the main display, that logic grabbed the **second** monitor. Always use the OS primary display instead.

Windows primary display = Settings → System → Display → “Make this my main display.”

```csharp
var bounds = Screen.PrimaryScreen?.Bounds
    ?? SystemInformation.VirtualScreen;
```

Fallback to the virtual desktop only if `PrimaryScreen` is missing.

## End-to-end flow

1. Ignore re-entry (`_screenshotBusy`).
2. Confirm WebView2 exists and the URL contains `chatgpt.com`.
3. Resolve primary-display bounds (pixels).
4. If the overlay is visible, hide it with the existing off-screen hide (`HidePrivateBrowserByHotkey`), wait ~180ms so the desktop redraws, capture, then restore. This keeps PrivateBrowser out of the shot even when it sits on the main display.
5. `Graphics.CopyFromScreen` into a 32bpp PNG.
6. Save PNG under `%TEMP%\PrivateBrowser\screenshot-{guid}.png`.
7. Put the image on the clipboard as WPF `BitmapSource` plus `PNG` bytes (retry clipboard lock `0x800401D0`).
8. Restore / activate the window and focus WebView2.
9. Attach the file to ChatGPT (CDP first, clipboard paste fallback).
10. Insert the analysis prompt with `send: false` and `replaceExisting: false`.
11. Delete the temp PNG after a short delay (CDP may still be reading it).

## Capture (GDI)

Project needs `<UseWindowsForms>true</UseWindowsForms>` (already set) so `System.Drawing` and `System.Windows.Forms.Screen` are available.

```csharp
var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bitmap))
{
    g.CopyFromScreen(
        bounds.Left, bounds.Top, 0, 0,
        bounds.Size, CopyPixelOperation.SourceCopy);
}
bitmap.Save(path, ImageFormat.Png);
```

Coordinates are **pixels**, not WPF DIPs. `Screen.Bounds` is already in pixels, which matches `CopyFromScreen`.

The window also uses `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` so the overlay is omitted from many capture APIs. Still hide the window during capture so a same-monitor overlay does not leave a hole or leak the assistant UI.

## Attach image to ChatGPT (WebView2)

ChatGPT.com has no public upload API. Use Chromium DevTools Protocol through WebView2, then paste as backup.

### Primary: `DOM.setFileInputFiles`

1. In page JS, find `input[type=file]` (prefer `accept` containing image/png/`*`). If none, click Attach / Add files / `composer-plus-btn`, wait, search again.
2. Mark the input: `data-pb-upload="1"`.
3. CDP:

```text
DOM.enable
DOM.getDocument  { "depth": 0, "pierce": true }
DOM.querySelector  { nodeId: <root>, selector: "input[type=\"file\"][data-pb-upload=\"1\"]" }
DOM.setFileInputFiles  { files: ["C:\\...\\screenshot.png"], nodeId: <id> }
```

Call via `CoreWebView2.CallDevToolsProtocolMethodAsync(method, jsonParams)`.

`nodeId == 0` means the input was not found.

### Fallback: clipboard image + Ctrl+V

1. Focus `#prompt-textarea` / `[data-testid="prompt-textarea"]` / `.ProseMirror[contenteditable=true]`.
2. CDP `Input.dispatchKeyEvent` Ctrl+V (`modifiers: 2`, `windowsVirtualKeyCode` 17 then 86).
3. Also send a real OS `SendInput` Ctrl+V so Chromium treats it as a user paste.

Do **not** send the message after paste.

## Insert prompt without sending

Reuse `PasteIntoChatGptAndSendAsync(text, send: false, replaceExisting: false)`.

- `send: false` — after the composer contains the prompt, return `{ method: "draft" }`. Do not click the send button and do not press Enter.
- `replaceExisting: false` — do not `selectAll` first. Place the caret at the end and `execCommand('insertText')` so an attached image is not wiped. ChatGPT usually keeps files in an attachment tray outside the ProseMirror text, but skipping select-all is still safer.

Composer insert rules that already exist for Paste & Send:

- Prefer `document.execCommand('insertText')` on ProseMirror. Do not set `textContent` (ChatGPT ignores it).
- Confirm the expected text is actually in the composer before considering success.

## UI and hotkey wiring (Windows)

| Item | Value |
| --- | --- |
| Hotkey id | `1006` (`HOTKEY_SCREENSHOT`) |
| Virtual key | `0x58` (`X`) |
| Modifiers | `MOD_CONTROL \| MOD_SHIFT \| MOD_NOREPEAT` |
| Register | `RegisterCaptionHotkeys` + unregister on close |
| WndProc | `WM_HOTKEY` → `CaptureScreenshotAndAttachToChatGptAsync` on the dispatcher |
| Button | `ScreenshotButton` in `MainWindow.xaml`, handler `ScreenshotButton_Click` |

WPF + WinForms name clashes: keep aliases (`WpfClipboard`, `WpfDataObject`, `WpfButton`, `Drawing`, `Forms`).

## Porting checklist (Mac / rewrite)

1. Capture **main display** bounds, not “the other monitor.”
2. Hide or exclude the assistant window, wait one frame, then capture.
3. Produce a PNG on disk and/or clipboard.
4. Attach to ChatGPT without sending:
   - Desktop WebView: CDP `DOM.setFileInputFiles` if you can reach the file input.
   - Otherwise focus composer and paste the image, then insert prompt text.
5. Never auto-send.
6. Gate on `chatgpt.com`.
7. Guard against double-trigger from the hotkey.

Mac notes: ScreenCaptureKit / `CGDisplayCreateImage` for the main `NSScreen`. WKWebView has no CDP file-input helper; clipboard paste into the composer is the realistic attach path. Use `Cmd+Shift+X` to match the other Mac hotkeys.

## Code map

| Piece | Location |
| --- | --- |
| Button | `MainWindow.xaml` (`ScreenshotButton`) |
| Hotkey constants / P/Invoke / prompt | `MainWindow.xaml.cs` (global hotkeys region) |
| Orchestration | `CaptureScreenshotAndAttachToChatGptAsync` |
| Primary bounds | `GetScreenshotBounds` |
| GDI capture | `CaptureScreenBounds` |
| Temp PNG | `SaveScreenshotPng` |
| Clipboard | `SetClipboardImageAsync` |
| CDP attach | `AttachScreenshotFileToChatGptAsync` |
| Paste fallback | `PasteClipboardImageIntoChatGptAsync`, `SendCtrlV` |
| Prompt insert | `PasteIntoChatGptAndSendAsync` |
| Overlay hide/restore | `HidePrivateBrowserByHotkey` / `RestorePrivateBrowserFromHotkey` |
