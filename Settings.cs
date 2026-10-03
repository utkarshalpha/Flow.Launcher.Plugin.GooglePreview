namespace Flow.Launcher.Plugin.GooglePreview
{
    public class Settings
    {
        public bool ShowSuggestions { get; set; } = true;

        // Google suggestions shown under what you typed, 1 to 5
        public int SuggestionCount { get; set; } = 5;

        public bool ShowChatGpt { get; set; } = true;

        // Show ChatGPT's answer in the preview; off means the row only opens the browser
        public bool ChatGptPreview { get; set; } = true;

        // true: preview loads only when you pick a result (arrow keys / click), so
        // half-typed text never reaches Google or ChatGPT.
        // false: it loads on its own once typing pauses for PreviewDelayMs.
        public bool PreviewOnSelect { get; set; } = true;

        public int PreviewDelayMs { get; set; } = 1000;

        public bool ShowHint { get; set; } = true;

        public string HintText { get; set; } = "Press ↓ to preview";

        // 100 = preview text matches Flow's result list (14px titles / 12px text)
        public int TextSizePercent { get; set; } = 100;

        // Off by default: the preview only gets your location if you opt in
        public bool AllowLocation { get; set; } = false;
    }
}
