using System;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using CCSuitePro.Services;
using System.Windows;
using System.Windows.Media;

namespace CCSuitePro
{
    public partial class SplashWindow : Window
    {
        private readonly WinterSnowfallEffect? _welcomeSnow;

        public SplashWindow()
        {
            InitializeComponent();
            bool welcome = ThemeManager.Current == ThemeVariant.WinterEdition &&
                           WinterWelcomePreference.ReadEnabled();
            if (!welcome) return;

            RegularSplashTag.Visibility = Visibility.Collapsed;
            WinterWelcomeIntro.Visibility = Visibility.Visible;
            WinterWelcomeGreeting.Text = LanguageService.Instance.CurrentLanguage switch
            {
                "es" => "Tu biblioteca, más limpia y brillante esta temporada.",
                "ko" => "이번 겨울, 모드 라이브러리를 더 깔끔하고 빛나게.",
                _ => "Your library, cleaner and brighter this season."
            };

            if (WinterSnowfallPreference.ReadEnabled())
            {
                WinterWelcomeSnowOverlay.Visibility = Visibility.Visible;
                _welcomeSnow = new WinterSnowfallEffect(WinterWelcomeSnowOverlay, this);
                _welcomeSnow.SetEnabled(true);
            }
            // Use the existing startup progress duration; no extra waiting or network access.
            Opacity = 0;
            Loaded += (_, _) => BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(430))
                { FillBehavior = FillBehavior.HoldEnd });
            Closed += (_, _) => _welcomeSnow?.Dispose();
        }

        public async Task RunStartupSequenceAsync()
        {
            await StepAsync(8, LanguageService.Instance["splash_initializing"], LanguageService.Instance["splash_loading_services"], 1);
            await StepAsync(35, LanguageService.Instance["splash_loading_settings"], LanguageService.Instance["splash_reading_preferences"], 1);
            await StepAsync(67, LanguageService.Instance["splash_applying_language"], LanguageService.Instance["splash_preparing_localized"], 2);
            await StepAsync(100, LanguageService.Instance["splash_preparing_interface"], LanguageService.Instance["splash_finalizing_dashboard"], 3);
            await Task.Delay(180);
        }

        private async Task StepAsync(double value, string stage, string detail, int completedSteps)
        {
            StageText.Text = stage;
            DetailText.Text = detail;
            SplashProgressBar.Value = value;
            PercentText.Text = $"{value:0}%";

            if (completedSteps >= 1)
            {
                Step1Dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Theme.Brush.50A4FF");
            }
            if (completedSteps >= 2)
            {
                Step2Dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Theme.Brush.50A4FF");
                Step2Text.Foreground = Brushes.White;
            }
            if (completedSteps >= 3)
            {
                Step3Dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Theme.Brush.50A4FF");
                Step3Text.Foreground = Brushes.White;
            }

            await Task.Delay(360);
        }
    }
}