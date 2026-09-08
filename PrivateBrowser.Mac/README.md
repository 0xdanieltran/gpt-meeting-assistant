# PrivateBrowser for macOS

This folder is the Mac app. The Windows project in `PrivateBrowser/` is unchanged.

## Build on a Mac

```bash
cd PrivateBrowser.Mac
dotnet restore
dotnet publish -c Release -r osx-arm64 --self-contained true
```

Intel Mac:

```bash
dotnet publish -c Release -r osx-x64 --self-contained true
```

The app is in `bin/Release/net10.0/osx-arm64/publish/`.

On first run, macOS will ask for **Microphone** and **Screen Recording** so live captions can hear you and the meeting. Screen Recording is also required for the Screenshot button.

## Hotkeys

Same letters as Windows, using Command instead of Control:

- Cmd+Shift+C copy latest caption
- Cmd+Shift+A copy all
- Cmd+Shift+S last 3 blocks + interview prompt, paste into ChatGPT
- Cmd+Shift+V paste clipboard into ChatGPT
- Cmd+Shift+Q hide/show
- Cmd+Shift+X capture the main display and attach it to ChatGPT as a draft (does not send)

Chrome extensions from the Windows app are not available in the Mac web view.
