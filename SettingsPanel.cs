using System;
using System.Windows;
using System.Windows.Controls;

namespace Flow.Launcher.Plugin.GooglePreview
{
    /// <summary>The plugin's page under Flow → Settings → Plugins → Google Preview.</summary>
    public class SettingsPanel : UserControl
    {
        private readonly Settings _settings;
        private readonly Action _save;

        public SettingsPanel(Settings settings, Action save)
        {
            _settings = settings;
            _save = save;

            var panel = new StackPanel { Margin = new Thickness(70, 14, 18, 14) };

            var suggestions = AddCheckBox(panel, "Show Google suggestions", settings.ShowSuggestions,
                v => settings.ShowSuggestions = v);

            var countRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 0, 0, 12) };
            countRow.Children.Add(new TextBlock { Text = "Number of suggestions", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            var count = new ComboBox { Width = 70 };
            for (var i = 1; i <= 5; i++) count.Items.Add(i);
            count.SelectedItem = Math.Clamp(settings.SuggestionCount, 1, 5);
            count.SelectionChanged += (_, _) =>
            {
                settings.SuggestionCount = (int)count.SelectedItem;
                _save();
            };
            count.IsEnabled = settings.ShowSuggestions;
            suggestions.Checked += (_, _) => count.IsEnabled = true;
            suggestions.Unchecked += (_, _) => count.IsEnabled = false;
            countRow.Children.Add(count);
            panel.Children.Add(countRow);

            AddCheckBox(panel, "Show \"Ask ChatGPT\" (preview shows ChatGPT's answer, no login needed)", settings.ShowChatGpt,
                v => settings.ShowChatGpt = v);

            AddCheckBox(panel, "Allow location in the preview (for \"near me\" searches)", settings.AllowLocation,
                v => settings.AllowLocation = v);

            panel.Children.Add(new TextBlock
            {
                Text = "Camera and microphone are always blocked.",
                Opacity = 0.7,
                Margin = new Thickness(0, 4, 0, 0),
            });

            Content = panel;
        }

        private CheckBox AddCheckBox(Panel panel, string text, bool value, Action<bool> set)
        {
            var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 0, 0, 12) };
            box.Checked += (_, _) => { set(true); _save(); };
            box.Unchecked += (_, _) => { set(false); _save(); };
            panel.Children.Add(box);
            return box;
        }
    }
}
