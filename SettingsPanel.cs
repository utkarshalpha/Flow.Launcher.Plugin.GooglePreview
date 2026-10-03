using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Flow.Launcher.Plugin.GooglePreview
{
    /// <summary>The plugin's page under Flow → Settings → Plugins → Google Preview.</summary>
    public class SettingsPanel : UserControl
    {
        private readonly Action _save;

        public SettingsPanel(Settings settings, Action save)
        {
            _save = save;
            var panel = new StackPanel { Margin = new Thickness(70, 14, 18, 14) };

            // Suggestions
            var suggestions = AddCheckBox(panel, "Show Google suggestions", settings.ShowSuggestions, v => settings.ShowSuggestions = v);
            var count = AddCombo(panel, "Number of suggestions", new object[] { 1, 2, 3, 4, 5 },
                Math.Clamp(settings.SuggestionCount, 1, 5), v => settings.SuggestionCount = (int)v);
            BindEnabled(suggestions, count);

            // ChatGPT
            var chatGpt = AddCheckBox(panel, "Show \"Ask ChatGPT\"", settings.ShowChatGpt, v => settings.ShowChatGpt = v);
            var chatGptPreview = AddCheckBox(panel, "Show ChatGPT's answer in the preview (no login needed)", settings.ChatGptPreview,
                v => settings.ChatGptPreview = v, indent: true);
            BindEnabled(chatGpt, chatGptPreview);

            // When the preview loads
            AddHeading(panel, "Load the preview");
            // Option 1: press → (each option's details sit directly under it)
            var onSelect = AddRadio(panel, "When I press → (recommended)", settings.PreviewOnSelect, () => settings.PreviewOnSelect = true);
            AddNote(panel, "Press → once after typing; then ↓ / ↑ load previews as you move. Half-typed text is never searched.");
            var hint = AddCheckBox(panel, "Show a hint until I press →", settings.ShowHint, v => settings.ShowHint = v, indent: true);
            var hintText = new TextBox { Text = settings.HintText, Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(52, 0, 0, 14) };
            hintText.TextChanged += (_, _) =>
            {
                settings.HintText = string.IsNullOrWhiteSpace(hintText.Text) ? "Press → to preview" : hintText.Text;
                _save();
            };
            panel.Children.Add(hintText);

            // Option 2: after a pause
            var afterPause = AddRadio(panel, "Automatically when I stop typing", !settings.PreviewOnSelect, () => settings.PreviewOnSelect = false);
            var delay = AddCombo(panel, "Wait after typing (ms)", new object[] { 500, 750, 1000, 1500, 2000, 3000 },
                settings.PreviewDelayMs, v => settings.PreviewDelayMs = (int)v);

            void SyncLoadOptions()
            {
                var pressMode = onSelect.IsChecked == true;
                hint.IsEnabled = pressMode;
                hintText.IsEnabled = pressMode && hint.IsChecked == true;
                delay.IsEnabled = !pressMode;
            }
            SyncLoadOptions();
            onSelect.Checked += (_, _) => SyncLoadOptions();
            afterPause.Checked += (_, _) => SyncLoadOptions();
            hint.Checked += (_, _) => SyncLoadOptions();
            hint.Unchecked += (_, _) => SyncLoadOptions();

            // Text size
            AddCombo(panel, "Preview text size (%)", new object[] { 70, 80, 90, 100, 110, 120, 130 },
                settings.TextSizePercent, v => settings.TextSizePercent = (int)v, indent: false);

            // Privacy
            AddHeading(panel, "Privacy");
            AddCheckBox(panel, "Allow location in the preview (for \"near me\" searches)", settings.AllowLocation, v => settings.AllowLocation = v);
            panel.Children.Add(new TextBlock { Text = "Camera and microphone are always blocked.", Opacity = 0.7 });

            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private static void AddNote(Panel panel, string text) =>
            panel.Children.Add(new TextBlock { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, -4, 0, 10) });

        private static void AddHeading(Panel panel, string text) =>
            panel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8) });

        private static void BindEnabled(ToggleButton source, UIElement target)
        {
            target.IsEnabled = source.IsChecked == true;
            source.Checked += (_, _) => target.IsEnabled = true;
            source.Unchecked += (_, _) => target.IsEnabled = false;
        }

        private CheckBox AddCheckBox(Panel panel, string text, bool value, Action<bool> set, bool indent = false)
        {
            var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(indent ? 26 : 0, 0, 0, 12) };
            box.Checked += (_, _) => { set(true); _save(); };
            box.Unchecked += (_, _) => { set(false); _save(); };
            panel.Children.Add(box);
            return box;
        }

        private RadioButton AddRadio(Panel panel, string text, bool value, Action select)
        {
            var radio = new RadioButton { Content = text, IsChecked = value, GroupName = "PreviewLoad", Margin = new Thickness(0, 0, 0, 10) };
            radio.Checked += (_, _) => { select(); _save(); };
            panel.Children.Add(radio);
            return radio;
        }

        private ComboBox AddCombo(Panel panel, string label, object[] items, int value, Action<object> set, bool indent = true)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(indent ? 26 : 0, 0, 0, 12) };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            var combo = new ComboBox { Width = 80 };
            foreach (var item in items) combo.Items.Add(item);
            combo.SelectedItem = Array.IndexOf(items, value) >= 0 ? value : items[0];
            combo.SelectionChanged += (_, _) => { set(combo.SelectedItem); _save(); };
            row.Children.Add(combo);
            panel.Children.Add(row);
            return combo;
        }
    }
}
