using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CCSuitePro.Services;
using CCSuitePro.ViewModels;

namespace CCSuitePro
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private bool _themePickerReady;
        private bool _winterSnowReady;
        private WinterSnowfallEffect? _winterSnow;
        private bool _winterWelcomeReady;
        private bool _winterLightsReady;
        private WinterAmbientEffect? _winterAmbient;
        private bool _winter43Checking;
        private string _winter43StatusKey = "winter43_status_idle";
        private string _winter43LatestTag = "";
        
        public MainWindow()
        {
            InitializeComponent();
            // Keyboard navigation is available in every theme and never changes Mods.
            PreviewKeyDown += NavigationShortcut_KeyDown;
            // Fit the initial window within the usable desktop area, including taskbar.
            var desktop = SystemParameters.WorkArea;
            Width = System.Math.Min(Width, System.Math.Max(MinWidth, desktop.Width - 44));
            Height = System.Math.Min(Height, System.Math.Max(MinHeight, desktop.Height - 44));
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            // Apply appearance after all XAML controls exist, before the window becomes visible.
            ThemePicker.SelectedIndex = CCSuitePro.Services.ThemeManager.Current switch
            {
                CCSuitePro.Services.ThemeVariant.NegroPremium => 1,
                CCSuitePro.Services.ThemeVariant.WinterEdition => 2,
                _ => 0
            };
            _themePickerReady = true;
            // The old Winter17 ornament layer now hosts lightweight falling snow.
            WinterSnowToggle.IsChecked = WinterSnowfallPreference.ReadEnabled();
            _winterSnow = new WinterSnowfallEffect(WinterSnowOverlay, this);
            _winterSnowReady = true;
            UpdateSnowToggleText();
            WinterLightsModePicker.SelectedIndex = (int)WinterAmbientPreference.ReadMode();
            _winterAmbient = new WinterAmbientEffect(WinterAmbientOverlay, this);
            _winterLightsReady = true;
            UpdateLightsModeText();
            WinterWelcomeToggle.IsChecked = WinterWelcomePreference.ReadEnabled();
            _winterWelcomeReady = true;
            UpdateWinterWelcomeToggleText();
            CCSuitePro.Services.LanguageService.Instance.PropertyChanged += WinterLanguageChanged;
            Closed += (_, _) =>
            {
                CCSuitePro.Services.LanguageService.Instance.PropertyChanged -= WinterLanguageChanged;
                PreviewKeyDown -= NavigationShortcut_KeyDown;
                _winterSnow?.Dispose();
                _winterAmbient?.Dispose();
            };
            UpdateWinterThemeDecoration();
            Winter43RefreshLabels();
            
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2)
                MaximizeWindow(sender, e);
            else
                DragMove();
        }

        private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (WindowState != WindowState.Normal) return;
            Width = System.Math.Max(MinWidth, Width + e.HorizontalChange);
        }
        private void ResizeBottom_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (WindowState != WindowState.Normal) return;
            Height = System.Math.Max(MinHeight, Height + e.VerticalChange);
        }
        private void ResizeCorner_DragDelta(object sender, DragDeltaEventArgs e)
        {
            ResizeRight_DragDelta(sender, e);
            ResizeBottom_DragDelta(sender, e);
        }
        private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseWindow(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        
        private void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_themePickerReady) return;
            var next = ThemePicker.SelectedIndex switch
            {
                1 => CCSuitePro.Services.ThemeVariant.NegroPremium,
                2 => CCSuitePro.Services.ThemeVariant.WinterEdition,
                _ => CCSuitePro.Services.ThemeVariant.Actual
            };
            if (next == CCSuitePro.Services.ThemeManager.Current) return;
            try { CCSuitePro.Services.ThemeManager.ApplyAndSave(next); UpdateWinterThemeDecoration(); }
            catch (System.Exception)
            {
                _themePickerReady = false;
                ThemePicker.SelectedIndex = CCSuitePro.Services.ThemeManager.Current switch
            {
                CCSuitePro.Services.ThemeVariant.NegroPremium => 1,
                CCSuitePro.Services.ThemeVariant.WinterEdition => 2,
                _ => 0
            };
                _themePickerReady = true;
                UpdateWinterThemeDecoration();
                MessageBox.Show(CCSuitePro.Services.LanguageService.Instance["theme_error"],
                    "CC Suite Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Falling snow is decorative only; it is not visible in other themes.
        private void UpdateWinterThemeDecoration()
        {
            bool winter = CCSuitePro.Services.ThemeManager.Current == CCSuitePro.Services.ThemeVariant.WinterEdition;
            WinterThemeTag.Visibility = winter ? Visibility.Visible : Visibility.Collapsed;
            WinterSnowToggle.IsEnabled = winter;
            bool showSnow = winter && WinterSnowToggle.IsChecked == true;
            WinterSnowOverlay.Visibility = showSnow ? Visibility.Visible : Visibility.Collapsed;
            WinterSnowOverlay.Opacity = showSnow ? 1.0 : 0.0;
            _winterSnow?.SetEnabled(showSnow);
            WinterWelcomeToggle.IsEnabled = winter;
            WinterWelcomeToggle.Visibility = winter ? Visibility.Visible : Visibility.Collapsed;
            WinterLightsModePanel.Visibility = winter ? Visibility.Visible : Visibility.Collapsed;
            WinterLightsModePicker.IsEnabled = winter;
            WinterLightsMode mode = winter ? (WinterLightsMode)System.Math.Clamp(WinterLightsModePicker.SelectedIndex, 0, 2)
                                           : WinterLightsMode.Off;
            WinterAmbientOverlay.Visibility = mode == WinterLightsMode.Off ? Visibility.Collapsed : Visibility.Visible;
            _winterAmbient?.SetMode(mode);
        }

        private void WinterSnowToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_winterSnowReady) return;
            WinterSnowfallPreference.SaveEnabled(WinterSnowToggle.IsChecked == true);
            UpdateWinterThemeDecoration();
        }

        private void WinterLightsModePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_winterLightsReady) return;
            WinterAmbientPreference.SaveMode((WinterLightsMode)System.Math.Clamp(WinterLightsModePicker.SelectedIndex, 0, 2));
            UpdateWinterThemeDecoration();
        }

        private void WinterWelcomeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_winterWelcomeReady) return;
            WinterWelcomePreference.SaveEnabled(WinterWelcomeToggle.IsChecked == true);
        }

        private void UpdateWinterWelcomeToggleText()
        {
            WinterWelcomeToggle.Content = CCSuitePro.Services.LanguageService.Instance.CurrentLanguage switch
            {
                "es" => "❄ Bienvenida navideña al iniciar (Winter Edition)",
                "ko" => "❄ 시작할 때 겨울 환영 화면 (Winter Edition)",
                _ => "❄ Winter welcome on startup (Winter Edition)"
            };
        }

        private void WinterLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CCSuitePro.Services.LanguageService.CurrentLanguage) ||
                e.PropertyName == "Item[]")
            {
                UpdateSnowToggleText();
                UpdateLightsModeText();
                UpdateWinterWelcomeToggleText();
                Winter43RefreshLabels();
            }
        }

        private void UpdateLightsModeText()
        {
            string language = CCSuitePro.Services.LanguageService.Instance.CurrentLanguage;
            WinterLightsModeLabel.Text = language switch
            {
                "es" => "✦ Luces navideñas (Winter Edition)",
                "ko" => "✦ 크리스마스 조명 (Winter Edition)",
                _ => "✦ Christmas lights (Winter Edition)"
            };
            WinterLightsOff.Content = language switch { "es" => "Desactivadas", "ko" => "끄기", _ => "Off" };
            WinterLightsSoft.Content = language switch { "es" => "Suaves", "ko" => "부드럽게", _ => "Soft" };
            WinterLightsDynamic.Content = language switch { "es" => "Dinámicas", "ko" => "역동적으로", _ => "Dynamic" };
        }

        private void UpdateSnowToggleText()
        {
            WinterSnowToggle.Content = CCSuitePro.Services.LanguageService.Instance.CurrentLanguage switch
            {
                "es" => "❄ Nieve animada (Winter Edition)",
                "ko" => "❄ 눈 내리는 효과 (Winter Edition)",
                _ => "❄ Animated snowfall (Winter Edition)"
            };
        }

        // Ctrl+1..7 follows the visible sidebar order. Ctrl+7 opens Settings;
        // these shortcuts also work with Spanish and Korean UI translations.
        private void NavigationShortcut_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || Keyboard.Modifiers != ModifierKeys.Control) return;
            int page = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 0, // Dashboard
                Key.D2 or Key.NumPad2 => 1, // All Files
                Key.D3 or Key.NumPad3 => 2, // Duplicates
                Key.D4 or Key.NumPad4 => 3, // Conflicts
                Key.D5 or Key.NumPad5 => 6, // Organize
                Key.D6 or Key.NumPad6 => 4, // Cleaner
                Key.D7 or Key.NumPad7 => 5, // Settings
                _ => -1
            };
            if (page < 0) return;
            MainTabs.SelectedIndex = page;
            e.Handled = true;
        }

        // Winter 43: solo consulta las publicaciones estables de nuestro repositorio.
        // No descarga ejecutables, no inicia procesos de actualizacion ni modifica Mods.
        private void Winter43RefreshLabels()
        {
            var language = CCSuitePro.Services.LanguageService.Instance;
            Winter43InstalledVersion.Text = language["winter43_installed_version"]
                + ": v" + GitHubReleaseChecker.InstalledVersion;
            var message = language[_winter43StatusKey];
            Winter43StatusText.Text = message.Replace("{version}", _winter43LatestTag);
        }

        private void Winter43SetStatus(string key, string latestTag = "")
        {
            _winter43StatusKey = key;
            _winter43LatestTag = latestTag;
            Winter43RefreshLabels();
        }

        private async void Winter43CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_winter43Checking) return;
            _winter43Checking = true;
            Winter43CheckButton.IsEnabled = false;
            Winter43ReleasesButton.Visibility = Visibility.Collapsed;
            Winter44StageButton.Visibility = Visibility.Collapsed;
            Winter43SetStatus("winter43_status_checking");
            try
            {
                var result = await GitHubReleaseChecker.CheckLatestStableAsync();
                if (!IsLoaded) return;
                switch (result.State)
                {
                    case Winter43ReleaseState.UpdateAvailable:
                        Winter43ReleasesButton.Visibility = Visibility.Visible;
                        Winter44StageButton.Visibility = Visibility.Visible;
                        Winter43SetStatus("winter43_status_available", result.Tag);
                        break;
                    case Winter43ReleaseState.UpToDate:
                        Winter43SetStatus("winter43_status_current", result.Tag);
                        break;
                    case Winter43ReleaseState.NoPublishedRelease:
                        Winter43SetStatus("winter43_status_none");
                        break;
                    default:
                        Winter43SetStatus("winter43_status_unsupported");
                        break;
                }
            }
            catch (System.Exception)
            {
                if (IsLoaded) Winter43SetStatus("winter43_status_error");
            }
            finally
            {
                _winter43Checking = false;
                if (IsLoaded) Winter43CheckButton.IsEnabled = true;
            }
        }

        // Winter44: una descarga explicitamente consentida, verificada y SIN instalacion.
        private bool _winter44Downloading;
        private async void Winter44Stage_Click(object sender, RoutedEventArgs e)
        {
            if (_winter44Downloading || string.IsNullOrWhiteSpace(_winter43LatestTag)) return;
            var language = LanguageService.Instance;
            if (MessageBox.Show(language["winter44_confirm"], "CC Suite Pro",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            string selectedTag = _winter43LatestTag;
            _winter44Downloading = true;
            Winter44StageButton.IsEnabled = false;
            Winter43CheckButton.IsEnabled = false;
            Winter43SetStatus("winter44_status_downloading", selectedTag);
            try
            {
                await GitHubUpdateStager.DownloadAndVerifyAsync(selectedTag);
                if (IsLoaded) Winter43SetStatus("winter44_status_verified", selectedTag);
            }
            catch (System.Exception)
            {
                if (IsLoaded) Winter43SetStatus("winter44_status_error", selectedTag);
            }
            finally
            {
                _winter44Downloading = false;
                if (IsLoaded)
                {
                    Winter44StageButton.IsEnabled = true;
                    Winter43CheckButton.IsEnabled = true;
                }
            }
        }

        private void Winter43OpenReleases_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // URL fija y confiable: nunca ejecutar un enlace recibido desde JSON remoto.
                Process.Start(new ProcessStartInfo(GitHubReleaseChecker.ReleasesPage)
                {
                    UseShellExecute = true
                });
            }
            catch (System.Exception)
            {
                Winter43SetStatus("winter43_status_open_failed");
            }
        }

        private void OpenTutorial_Click(object sender, RoutedEventArgs e) => FirstRunTutorial.Show(this);

        private void SwitchTab(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tagStr && int.TryParse(tagStr, out int index))
                MainTabs.SelectedIndex = index;
        }
    }
}
