# Google Preview for Flow Launcher
<img width="1535" height="863" alt="image" src="https://github.com/user-attachments/assets/ff6165df-7549-42ab-825d-c5c460f4f55e" />


Google suggestions in [Flow Launcher](https://www.flowlauncher.com), with the real Google results page shown right in the preview panel.

- What you type and up to 5 Google suggestions, each with a live results preview
- AI Overview, maps, places, shopping and the AI Mode / Images / Videos tabs inside the preview
- An "Ask ChatGPT" row right after your search
- Compact view: Google's menu, sign-in, search bar, mic and Lens buttons are hidden
- "← Back to results" steps back one page (for example out of AI Mode or an expanded map)
- Enter opens the full search in your default browser; links clicked in the preview open there too
- Apps that match what you type still rank above the web results

## Install

In Flow Launcher:

```
pm install Google Preview
```

Or download the zip from [Releases](https://github.com/utkarshalpha/Flow.Launcher.Plugin.GooglePreview/releases) and run `pm install <path to zip>`.

Requires Flow Launcher 2.0 or newer and the Microsoft Edge WebView2 Runtime (included with Windows 11).

## Usage

Press your Flow hotkey (default `Alt + Space`) and type. Arrow onto a Google result to see its results page in the preview (`F1` toggles the preview panel).

## Notes

- The preview is an embedded Edge WebView2 using Google's mobile layout. It keeps its own cookies and permissions in `%LOCALAPPDATA%\FlowLauncher\GooglePreviewWebView2`.
- Location access is allowed so "near me" searches work.
- Voice search is hidden because Google's voice input doesn't work inside WebView2.
- If Google changes its page layout, some hidden parts may reappear until the plugin is updated.

Not affiliated with or endorsed by Google or OpenAI.

## Build

```
dotnet publish -c Release -o out
```

Copy the contents of `out` into `%APPDATA%\FlowLauncher\Plugins\GooglePreview` and restart Flow.

## License

MIT
