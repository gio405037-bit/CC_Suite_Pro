using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CCSuitePro.Services;

namespace CCSuitePro
{
    // A non-destructive first-run guide. No Mods, hashes or scan records are accessed.
    internal static class FirstRunTutorial
    {
        private static string DataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCSuitePro");
        private static string MarkerFile => Path.Combine(DataDirectory, "FirstRun", "tutorial-v1.complete");
        private static string ExistingSettingsFile => Path.Combine(DataDirectory, "settings.json");

        internal static void ShowIfNew(Window owner)
        {
            // Existing installations must not be interrupted by an upgrade.
            // A genuinely new profile has neither a completion marker nor settings.
            if (File.Exists(MarkerFile) || File.Exists(ExistingSettingsFile)) return;
            if (owner is MainWindow main) main.StartInteractiveTour(true);
        }

        internal static void Show(Window owner)
        {
            var tutorial = new FirstRunTutorialWindow { Owner = owner };
            tutorial.ShowDialog();
            // Keep the Settings language selection in sync with the guide's picker.
            if (owner is MainWindow main)
            {
                main.SyncGuideLanguage();
                SettingsService.Current.Language = LanguageService.Instance.CurrentLanguage;
                SettingsService.Save();
                if (tutorial.RestartTourRequested) main.StartInteractiveTour(false);
            }
        }

        internal static void Complete()
        {
            string folder = Path.GetDirectoryName(MarkerFile)!;
            Directory.CreateDirectory(folder);
            File.WriteAllText(MarkerFile, "1");
            // Persist only the language the user explicitly chose in the guide.
            SettingsService.Current.Language = LanguageService.Instance.CurrentLanguage;
            SettingsService.Save();
        }
    }

    internal sealed class FirstRunTutorialWindow : Window
    {
        private const int StepCount = 5;
        private readonly TextBlock _stepNumber = new();
        private readonly TextBlock _stepTitle = new();
        private readonly TextBlock _stepDescription = new();
        private readonly TextBlock _subtitle = new();
        private readonly Button _back = new();
        private readonly Button _next = new();
        private readonly Button _skip = new();
        private readonly Button _restart = new();
        internal bool RestartTourRequested { get; private set; }
        private readonly ComboBox _languagePicker = new();
        private readonly ProgressBar _progress = new();
        private int _step;

        internal FirstRunTutorialWindow()
        {
            Title = "CC Suite Pro";
            Width = 660;
            Height = 485;
            MinWidth = 560;
            MinHeight = 430;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.SingleBorderWindow;
            SourceInitialized += (_, _) => DarkWindowChrome.Apply(this);
            Background = new SolidColorBrush(Color.FromRgb(10, 18, 33));
            SetResourceReference(Window.BackgroundProperty, "Theme.Brush.0A1221");
            Foreground = Brushes.White;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");

            var panel = new Grid { Margin = new Thickness(28) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var headline = new TextBlock
            {
                Text = "CC Suite Pro", FontSize = 22, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White
            };
            header.Children.Add(headline);
            _languagePicker.MinWidth = 120;
            _languagePicker.Style = Application.Current.TryFindResource("CCSuiteDarkComboBox") as Style;
            _languagePicker.Margin = new Thickness(12, 0, 0, 0);
            _languagePicker.Items.Add("English");
            _languagePicker.Items.Add("Español");
            _languagePicker.Items.Add("한국어");
            _languagePicker.SelectedIndex = LanguageService.Instance.CurrentLanguage switch
            {
                "es" => 1, "ko" => 2, _ => 0
            };
            _languagePicker.SelectionChanged += (_, _) =>
            {
                if (_languagePicker.SelectedIndex < 0) return;
                LanguageService.Instance.CurrentLanguage = _languagePicker.SelectedIndex switch
                {
                    1 => "es", 2 => "ko", _ => "en"
                };
                UpdateView();
            };
            Grid.SetColumn(_languagePicker, 1);
            header.Children.Add(_languagePicker);
            Grid.SetRow(header, 0);
            panel.Children.Add(header);

            _subtitle.FontSize = 13;
            _subtitle.Foreground = new SolidColorBrush(Color.FromRgb(164, 187, 221));
            _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.A4BBDD");
            _subtitle.Margin = new Thickness(0, 0, 0, 14);
            _subtitle.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(_subtitle, 1);
            panel.Children.Add(_subtitle);

            _progress.Minimum = 0;
            _progress.Maximum = StepCount;
            _progress.Height = 6;
            _progress.Foreground = new SolidColorBrush(Color.FromRgb(70, 151, 255));
            _progress.SetResourceReference(Control.ForegroundProperty, "Theme.Brush.4697FF");
            _progress.Background = new SolidColorBrush(Color.FromRgb(34, 55, 83));
            _progress.SetResourceReference(Control.BackgroundProperty, "Theme.Brush.223753");
            _progress.Margin = new Thickness(0, 0, 0, 18);
            Grid.SetRow(_progress, 2);
            panel.Children.Add(_progress);

            var content = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                Background = new SolidColorBrush(Color.FromRgb(17, 30, 53)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(44, 72, 110)),
                BorderThickness = new Thickness(1)
            };
            var contentStack = new StackPanel();
            _stepNumber.FontSize = 12;
            _stepNumber.Foreground = new SolidColorBrush(Color.FromRgb(108, 174, 255));
            _stepNumber.Margin = new Thickness(0, 0, 0, 12);
            contentStack.Children.Add(_stepNumber);
            _stepTitle.FontSize = 23;
            _stepTitle.FontWeight = FontWeights.SemiBold;
            _stepTitle.Foreground = Brushes.White;
            _stepTitle.TextWrapping = TextWrapping.Wrap;
            _stepTitle.Margin = new Thickness(0, 0, 0, 16);
            contentStack.Children.Add(_stepTitle);
            _stepDescription.FontSize = 15;
            _stepDescription.Foreground = new SolidColorBrush(Color.FromRgb(202, 217, 239));
            _stepDescription.TextWrapping = TextWrapping.Wrap;
            _stepDescription.LineHeight = 24;
            contentStack.Children.Add(_stepDescription);
            content.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.111E35");
            content.SetResourceReference(Border.BorderBrushProperty, "Theme.Brush.2C486E");
            _stepNumber.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.6CAEFF");
            _stepDescription.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.CAD9EF");
            content.Child = new ScrollViewer
            {
                Content = contentStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(content, 3);
            panel.Children.Add(content);

            var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StyleButton(_skip, false);
            _skip.Click += (_, _) => Finish();
            Grid.SetColumn(_skip, 0);
            footer.Children.Add(_skip);
            StyleButton(_restart, false);
            _restart.Margin = new Thickness(6, 0, 9, 0);
            _restart.Click += (_, _) => { RestartTourRequested = true; Close(); };
            Grid.SetColumn(_restart, 2);
            footer.Children.Add(_restart);
            StyleButton(_back, false);
            _back.Margin = new Thickness(0, 0, 9, 0);
            _back.Click += (_, _) => { if (_step > 0) _step--; UpdateView(); };
            Grid.SetColumn(_back, 3);
            footer.Children.Add(_back);
            StyleButton(_next, true);
            _next.MinWidth = 136;
            _next.Click += (_, _) =>
            {
                if (_step == StepCount - 1) Finish();
                else { _step++; UpdateView(); }
            };
            Grid.SetColumn(_next, 4);
            footer.Children.Add(_next);
            Grid.SetRow(footer, 4);
            panel.Children.Add(footer);
            Content = panel;
            UpdateView();
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.MinHeight = 38;
            button.Padding = new Thickness(14, 5, 14, 5);
            button.Cursor = Cursors.Hand;
            button.FontWeight = FontWeights.SemiBold;
            button.Foreground = Brushes.White;
            button.Background = primary
                ? new SolidColorBrush(Color.FromRgb(32, 97, 230))
                : new SolidColorBrush(Color.FromRgb(34, 51, 76));
            button.SetResourceReference(Control.BackgroundProperty, primary ? "Theme.Brush.2061E6" : "Theme.Brush.22334C");
            button.BorderThickness = new Thickness(0);
        }

        private void UpdateView()
        {
            var language = LanguageService.Instance;
            _subtitle.Text = language["onboarding_subtitle"];
            _stepNumber.Text = string.Format("{0} / {1}", _step + 1, StepCount);
            _stepTitle.Text = language[$"onboarding_step_{_step + 1}_title"];
            _stepDescription.Text = language[$"onboarding_step_{_step + 1}_body"];
            _progress.Value = _step + 1;
            _back.Content = language["onboarding_back"];
            _back.IsEnabled = _step > 0;
            _next.Content = language[_step == StepCount - 1 ? "onboarding_finish" : "onboarding_next"];
            _skip.Content = language["onboarding_close"];
            _restart.Content = language["tour_start_again"];
        }

        private void Finish() => Close();
    }
}
