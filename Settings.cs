namespace Flow.Launcher.Plugin.GooglePreview
{
    public class Settings
    {
        public bool ShowSuggestions { get; set; } = true;

        // Google suggestions shown under what you typed, 1 to 5
        public int SuggestionCount { get; set; } = 5;

        public bool ShowChatGpt { get; set; } = true;

        // Off by default: the preview only gets your location if you opt in
        public bool AllowLocation { get; set; } = false;
    }
}
