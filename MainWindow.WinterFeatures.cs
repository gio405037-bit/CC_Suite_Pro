using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CCSuitePro.Services;

namespace CCSuitePro
{
    public partial class MainWindow
    {
        private bool _winterToolsNavigationReady;

        // Both the sidebar and Dashboard quick actions navigate to native MainTabs pages.
        // The former floating WinterToolsWindow is retained for rollback compatibility but is not opened.
        private void OpenTimeCapsule_Click(object sender, RoutedEventArgs e) => NavigateWinterTool(0);
        private void OpenModGuardian_Click(object sender, RoutedEventArgs e) => NavigateWinterTool(1);

        private void NavigateWinterTool(int tool)
        {
            if (tool is not (0 or 1)) return;
            if (!_winterToolsNavigationReady)
            {
                MainTabs.SelectionChanged += WinterToolsTabChanged;
                _winterToolsNavigationReady = true;
            }

            // Rebuild on navigation so the pane uses the current theme/language and fresh history.
            ContentControl host = tool == 0 ? WinterCapsuleHost : WinterGuardianHost;
            host.Content = new WinterToolsPanel(_viewModel, tool, NavigateWinterTool);
            MainTabs.SelectedIndex = tool == 0 ? 8 : 9;
            RefreshWinterNavigation();
            Dispatcher.BeginInvoke(new Action(RefreshWinterNavigation), DispatcherPriority.Loaded);
        }

        private void WinterToolsTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;
            // Run after the legacy UX24 navigation updater.
            Dispatcher.BeginInvoke(new Action(RefreshWinterNavigation), DispatcherPriority.Loaded);
        }

        private void RefreshWinterNavigation()
        {
            if (!_winterToolsNavigationReady) return;
            int selected = MainTabs.SelectedIndex;
            bool capsule = selected == 8;
            bool guardian = selected == 9;
            SetWinterNavState(WinterCapsuleNav, capsule);
            SetWinterNavState(WinterGuardianNav, guardian);
            if (!capsule && !guardian) return;

            // UX24 covers pages 0..7; label these new pages explicitly.
            string lang = LanguageService.Instance.CurrentLanguage;
            UX25_PageTitle.Text = capsule ? lang switch
            {
                "es" => "Cápsula del tiempo", "ko" => "타임 캡슐", _ => "Time Capsule"
            } : "Mod Guardian";
            UX25_PageDescription.Text = capsule ? lang switch
            {
                "es" => "Conserva las estadísticas de tu biblioteca y compara su evolución.",
                "ko" => "라이브러리 통계를 기록하고 변화를 비교하세요.",
                _ => "Save library statistics and compare their history."
            } : lang switch
            {
                "es" => "Compara escaneos y revisa los cambios sin modificar tus Mods.",
                "ko" => "모드를 변경하지 않고 검사 결과를 비교하세요.",
                _ => "Compare scans and review changes without modifying Mods."
            };
        }

        private static void SetWinterNavState(Button button, bool active)
        {
            bool winter = ThemeManager.Current == ThemeVariant.WinterEdition;
            string bg = active ? (winter ? "#542A35" : "#163A70") : "#00000000";
            string border = active ? (winter ? "#B66A76" : "#3569B0") : "#00000000";
            button.Background = (Brush)new BrushConverter().ConvertFromString(bg)!;
            button.BorderBrush = (Brush)new BrushConverter().ConvertFromString(border)!;
            button.Foreground = (Brush)new BrushConverter().ConvertFromString(active ? "#FFFFFF" : "#A9BBD8")!;
        }
    }
}
