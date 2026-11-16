using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CCSuitePro.Services;

namespace CCSuitePro
{
    public partial class MainWindow
    {
        private const int TourStepCount = 8;
        private int _tourStep = -1;
        private int _previousTourTab;
        private bool _tourIsFirstRun;
        private bool _tourIsUpdatingLanguage;
        private int _previousAnimatedTourStep = -1;
        private Point _previousCardPosition;
        private bool _hasCardPosition;

        // Passive tour: never invokes SelectFolder, Scan, Organizer or quarantine commands.
        internal void StartInteractiveTour(bool firstRun)
        {
            if (_tourStep >= 0) return;
            _previousTourTab = MainTabs.SelectedIndex;
            _tourIsFirstRun = firstRun;
            _tourStep = 0;
            _previousAnimatedTourStep = -1;
            _hasCardPosition = false;
            MainTabs.SelectedIndex = 0; // show the Choose Folder target on the dashboard
            TourOverlay.Visibility = Visibility.Visible;
            ShowTourStep();
            Dispatcher.BeginInvoke(new Action(PlaceTour), DispatcherPriority.Loaded);
        }

        private FrameworkElement TourTarget(int step) => step switch
        {
            0 => Nav0,
            1 => ChooseFolderTourTarget,
            2 => RefreshScanTourTarget,
            3 => Nav1,
            4 => Nav2,
            5 => Nav6,
            6 => Nav5,
            7 => BackupTourTarget,
            _ => Nav0
        };

        private void ShowTourStep()
        {
            if (_tourStep < 0) return;
            // Dashboard target is always in the visual tree; no navigation or real action required.
            if (MainTabs.SelectedIndex != 0) MainTabs.SelectedIndex = 0;
            // Keep the highlighted navigation control visible on smaller screens.
            TourTarget(_tourStep).BringIntoView();
            var locale = LanguageService.Instance;
            TourCounter.Text = string.Format("{0} / {1}", _tourStep + 1, TourStepCount);
            // Keep the existing 3-language strings while skipping the hidden AI step (6).
            int localizedStep = _tourStep < 5 ? _tourStep + 1 : _tourStep + 2;
            TourStepTitle.Text = locale[$"tour_step_{localizedStep}_title"];
            TourStepBody.Text = locale[$"tour_step_{localizedStep}_body"];
            TourSkip.Content = locale["tour_skip"];
            TourBack.Content = locale["tour_back"];
            TourBack.IsEnabled = _tourStep > 0;
            TourNext.Content = locale[_tourStep == TourStepCount - 1 ? "tour_finish" : "tour_next"];
            _tourIsUpdatingLanguage = true;
            try
            {
                int languageIndex = locale.CurrentLanguage switch { "es" => 1, "ko" => 2, _ => 0 };
                if (TourLanguagePicker.SelectedIndex != languageIndex)
                    TourLanguagePicker.SelectedIndex = languageIndex;
            }
            finally { _tourIsUpdatingLanguage = false; }
            PlaceTour();
            Dispatcher.BeginInvoke(new Action(PlaceTour), DispatcherPriority.Loaded);
        }

        private void PlaceTour()
        {
            if (_tourStep < 0 || TourOverlay.Visibility != Visibility.Visible) return;
            var target = TourTarget(_tourStep);
            if (!target.IsVisible || target.ActualWidth <= 0 || target.ActualHeight <= 0) return;
            double width = TourOverlay.ActualWidth;
            double height = TourOverlay.ActualHeight;
            if (width <= 0 || height <= 0) return;
            Rect raw;
            try { raw = target.TransformToVisual(TourOverlay).TransformBounds(
                new Rect(0, 0, target.ActualWidth, target.ActualHeight)); }
            catch (InvalidOperationException) { return; }

            var focus = new Rect(
                Math.Max(5, raw.Left - 7), Math.Max(5, raw.Top - 6),
                Math.Max(20, raw.Width + 14), Math.Max(20, raw.Height + 12));
            if (focus.Right > width - 5) focus.Width = Math.Max(12, width - 5 - focus.Left);
            if (focus.Bottom > height - 5) focus.Height = Math.Max(12, height - 5 - focus.Top);

            // Even-odd fill leaves a real spotlight aperture showing the original control.
            var shade = new GeometryGroup { FillRule = FillRule.EvenOdd };
            shade.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));
            shade.Children.Add(new RectangleGeometry(new Rect(focus.X, focus.Y, focus.Width, focus.Height), 12, 12));
            TourShade.Data = shade;
            TourFocusBorder.Width = focus.Width;
            TourFocusBorder.Height = focus.Height;
            Canvas.SetLeft(TourFocusBorder, focus.Left);
            Canvas.SetTop(TourFocusBorder, focus.Top);

            double cardWidth = Math.Min(390, Math.Max(270, width - 40));
            TourCard.Width = cardWidth;
            TourCard.Measure(new Size(cardWidth, double.PositiveInfinity));
            double cardHeight = Math.Max(180, TourCard.DesiredSize.Height);
            double gap = 27;
            bool cardRight = focus.Right + gap + cardWidth <= width - 14;
            bool cardLeft = focus.Left - gap - cardWidth >= 14;
            double cardX, cardY;
            if (cardRight) { cardX = focus.Right + gap; cardY = focus.Top - 20; }
            else if (cardLeft) { cardX = focus.Left - gap - cardWidth; cardY = focus.Top - 20; }
            else if (focus.Bottom + gap + cardHeight <= height - 10)
            { cardX = focus.Left; cardY = focus.Bottom + gap; }
            else { cardX = focus.Left; cardY = focus.Top - gap - cardHeight; }
            cardX = Math.Max(12, Math.Min(cardX, width - cardWidth - 12));
            cardY = Math.Max(12, Math.Min(cardY, height - cardHeight - 12));
            // Subtle motion only when changing steps, never on resize/language refresh.
            bool animateCard = _hasCardPosition && _previousAnimatedTourStep != _tourStep;
            TourCard.BeginAnimation(Canvas.LeftProperty, null);
            TourCard.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(TourCard, cardX);
            Canvas.SetTop(TourCard, cardY);
            if (animateCard)
            {
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                var duration = new Duration(TimeSpan.FromMilliseconds(190));
                TourCard.BeginAnimation(Canvas.LeftProperty,
                    new DoubleAnimation(_previousCardPosition.X, cardX, duration) { EasingFunction = easing });
                TourCard.BeginAnimation(Canvas.TopProperty,
                    new DoubleAnimation(_previousCardPosition.Y, cardY, duration) { EasingFunction = easing });
            }
            _previousCardPosition = new Point(cardX, cardY);
            _hasCardPosition = true;
            _previousAnimatedTourStep = _tourStep;

            // Arrow points from the highlighted UI toward the help card.
            double arrowX, arrowY;
            if (cardRight) { TourArrow.Text = "➜"; arrowX = focus.Right + 1; arrowY = focus.Top + 3; }
            else if (cardLeft) { TourArrow.Text = "⬅"; arrowX = focus.Left - 35; arrowY = focus.Top + 3; }
            else if (cardY > focus.Bottom) { TourArrow.Text = "⬇"; arrowX = focus.Left + 6; arrowY = focus.Bottom; }
            else { TourArrow.Text = "⬆"; arrowX = focus.Left + 6; arrowY = focus.Top - 40; }
            Canvas.SetLeft(TourArrow, Math.Max(5, arrowX));
            Canvas.SetTop(TourArrow, Math.Max(5, arrowY));
            if (animateCard)
            {
                TourArrow.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(180)));
            }
        }

        internal void SyncGuideLanguage()
        {
            _viewModel.SelectedLanguageIndex = LanguageService.Instance.CurrentLanguage switch
            {
                "es" => 1, "ko" => 2, _ => 0
            };
        }

        private void EndTour()
        {
            bool shouldSaveFirstRun = _tourIsFirstRun;
            _tourStep = -1;
            _tourIsFirstRun = false;
            TourOverlay.Visibility = Visibility.Collapsed;
            if (_previousTourTab >= 0 && _previousTourTab < MainTabs.Items.Count)
                MainTabs.SelectedIndex = _previousTourTab;
            try
            {
                SyncGuideLanguage();
                if (shouldSaveFirstRun) FirstRunTutorial.Complete();
                else
                {
                    SettingsService.Current.Language = LanguageService.Instance.CurrentLanguage;
                    SettingsService.Save();
                }
            }
            catch (Exception)
            {
                MessageBox.Show(LanguageService.Instance["onboarding_save_failed"], "CC Suite Pro",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void TourNext_Click(object sender, RoutedEventArgs e)
        {
            if (_tourStep < 0) return;
            if (_tourStep == TourStepCount - 1) EndTour();
            else { _tourStep++; ShowTourStep(); }
        }
        private void TourBack_Click(object sender, RoutedEventArgs e)
        {
            if (_tourStep > 0) { _tourStep--; ShowTourStep(); }
        }
        private void TourSkip_Click(object sender, RoutedEventArgs e) => EndTour();
        private void TourOverlay_SizeChanged(object sender, SizeChangedEventArgs e) => PlaceTour();
        private void TourLanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_tourIsUpdatingLanguage || _tourStep < 0) return;
            LanguageService.Instance.CurrentLanguage = TourLanguagePicker.SelectedIndex switch
            {
                1 => "es", 2 => "ko", _ => "en"
            };
            SyncGuideLanguage();
            ShowTourStep();
        }
    }
}
