using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace CCSuitePro
{
    public partial class MainWindow
    {
        private void UX24_OnLoaded(object sender, RoutedEventArgs e)
        {
            CCSuitePro.Services.LanguageService.Instance.PropertyChanged -= UX24_OnLanguageChanged;
            CCSuitePro.Services.LanguageService.Instance.PropertyChanged += UX24_OnLanguageChanged;
            Closed -= UX24_OnClosed;
            Closed += UX24_OnClosed;
            Dispatcher.BeginInvoke(new Action(UX24_RefreshNavigation), DispatcherPriority.Loaded);
        }


        private void UX24_OnClosed(object? sender, EventArgs e)
        {
            CCSuitePro.Services.LanguageService.Instance.PropertyChanged -= UX24_OnLanguageChanged;
        }

        private void UX24_OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "Item[]" && e.PropertyName != nameof(CCSuitePro.Services.LanguageService.CurrentLanguage)) return;
            if (!Dispatcher.CheckAccess())
                Dispatcher.BeginInvoke(new Action(UX24_RefreshNavigation), DispatcherPriority.Normal);
            else
                UX24_RefreshNavigation();
        }

        private void UX24_TabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;
            UX24_RefreshNavigation();
        }

        private void UX24_RefreshNavigation()
        {
            Button[] buttons = { Nav0, Nav1, Nav2, Nav3, Nav4, Nav5, Nav6, Nav7 };
            var locale = CCSuitePro.Services.LanguageService.Instance;
            string[] pageNames = { locale["dashboard"], locale["all_files"], locale["duplicates"], locale["conflicts"], locale["cleaner"], locale["settings"], locale["organize"], locale["ui_ai_center_1cd0d"] };
            string[] pageHints = { locale["nav_desc_dashboard"], locale["nav_desc_files"], locale["nav_desc_duplicates"], locale["nav_desc_conflicts"], locale["nav_desc_cleaner"], locale["nav_desc_settings"], locale["nav_desc_organize"], locale["nav_desc_ai"] };
            int selected = MainTabs.SelectedIndex;
            if (selected >= 0 && selected < pageNames.Length)
            {
                if (UX25_PageTitle != null) UX25_PageTitle.Text = pageNames[selected];
                if (UX25_PageDescription != null) UX25_PageDescription.Text = pageHints[selected];
            }
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                bool isActive = MainTabs.SelectedIndex == i;
                // Use theme-bound brushes so selecting Negro Premium recolors even an
                // already selected navigation entry without changing the selected tab.
                if (isActive)
                {
                    buttons[i].SetResourceReference(Control.BackgroundProperty, "CCSuite.Nav.SelectedBackground");
                    buttons[i].Foreground = Brushes.White;
                    buttons[i].SetResourceReference(Control.BorderBrushProperty, "CCSuite.Nav.SelectedBorder");
                }
                else
                {
                    buttons[i].Background = Brushes.Transparent;
                    buttons[i].SetResourceReference(Control.ForegroundProperty, "CCSuite.Nav.TextInactive");
                    buttons[i].BorderBrush = Brushes.Transparent;
                }
            }
        }

        private void UX24_ResizeRight(object sender, DragDeltaEventArgs e)
        {
            if (WindowState != WindowState.Normal) return;
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        }

        private void UX24_ResizeBottom(object sender, DragDeltaEventArgs e)
        {
            if (WindowState != WindowState.Normal) return;
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
        }

        private void UX24_ResizeCorner(object sender, DragDeltaEventArgs e)
        {
            UX24_ResizeRight(sender, e);
            UX24_ResizeBottom(sender, e);
        }
    }
}