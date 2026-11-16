using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CCSuitePro.Services;
using CCSuitePro.ViewModels;
using Microsoft.Win32;

namespace CCSuitePro
{
    /// <summary>Opt-in, read-only seasonal debut of two permanent library tools.</summary>
    public sealed class WinterToolsWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly Brush _background;
        private readonly Brush _surface;
        private readonly Brush _foreground = ColorBrush("#F3F7FD");
        private readonly Brush _muted = ColorBrush("#ABB9CE");
        private readonly Brush _accent;
        private readonly bool _winter;
        private readonly StackPanel _body = new() { Margin = new Thickness(4) };
        private readonly Button _capsuleTab;
        private readonly Button _guardianTab;
        private readonly int _startTab;
        private DockPanel _page = null!;
        private Border _hero = null!;
        private bool _layoutReady;
        private bool _suppressUserResize;
        private bool _userSized;
        private bool _autoFitQueued;

        // Stores window dimensions only, never scanned paths, filenames or Mod data.
        private static string WindowSizeFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CCSuitePro", "Settings", "WinterToolsWindowSize.json");

        private static Brush ColorBrush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
        private static Color ColorFrom(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
        private static string T(string en, string es, string ko) => LanguageService.Instance.CurrentLanguage switch
        {
            "es" => es, "ko" => ko, _ => en
        };

        public WinterToolsWindow(MainViewModel vm, int startTab)
        {
            _vm = vm;
            _startTab = startTab;
            bool premium = ThemeManager.Current == ThemeVariant.NegroPremium;
            _winter = ThemeManager.Current == ThemeVariant.WinterEdition;
            _accent = ColorBrush(_winter ? "#D6757F" : "#4D90FF");
            _background = _winter
                ? new LinearGradientBrush(ColorFrom("#140F13"), ColorFrom("#21171D"), 45)
                : ColorBrush(premium ? "#090B0F" : "#0C1426");
            _surface = ColorBrush(_winter ? "#2B2229" : premium ? "#17191E" : "#15233A");
            Title = "CC Suite Pro — Winter Edition";
            // Compact by default, but fully resizable. Clamp to the available desktop area.
            // The history/comparison content stays in a vertical ScrollViewer below the fixed header.
            double safeWidth = Math.Max(420, SystemParameters.WorkArea.Width - 48);
            double safeHeight = Math.Max(400, SystemParameters.WorkArea.Height - 48);
            Width = Math.Min(760, safeWidth);
            Height = Math.Min(startTab == 0 ? 520 : 560, safeHeight);
            MinWidth = Math.Min(620, Width);
            MinHeight = Math.Min(400, Height);
            ResizeMode = ResizeMode.CanResizeWithGrip;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = _background;
            Foreground = _foreground;
            FontFamily = new FontFamily("Segoe UI");

            // A user-resized dialog keeps its dimensions across launches.
            // If no saved dimensions exist, the dialog fits its content automatically.
            if (TryReadWindowSize(out double storedWidth, out double storedHeight))
            {
                Width = Math.Clamp(storedWidth, MinWidth, safeWidth);
                Height = Math.Clamp(storedHeight, MinHeight, safeHeight);
                _userSized = true;
            }
            Loaded += (_, _) => DarkWindowChrome.Apply(this);
            ContentRendered += (_, _) =>
            {
                if (_layoutReady) return;
                _layoutReady = true;
                QueueAutoFit();
            };
            SizeChanged += (_, _) =>
            {
                if (_layoutReady && !_suppressUserResize && WindowState == WindowState.Normal)
                    _userSized = true;
            };
            Closed += (_, _) => SaveWindowSizeIfCustomized();

            _page = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };
            Content = _page;
            var top = new StackPanel { Margin = new Thickness(0) };
            _hero = new Border
            {
                Padding = new Thickness(16, 11, 16, 12),
                Margin = new Thickness(0, 0, 0, 9),
                CornerRadius = new CornerRadius(15),
                BorderBrush = ColorBrush(_winter ? "#88515B" : "#34435B"),
                BorderThickness = new Thickness(1),
                Background = _winter
                    ? new LinearGradientBrush(ColorFrom("#4B2833"), ColorFrom("#1B171D"), 22)
                    : _background,
                Child = top
            };
            DockPanel.SetDock(_hero, Dock.Top);
            _page.Children.Add(_hero);
            if (_winter)
                top.Children.Add(Label("✦  ❄  ✧     WINTER COLLECTION 2026", 12,
                    ColorBrush("#F4BBC4"), FontWeights.SemiBold));
            top.Children.Add(Label("CC SUITE PRO", 12, _accent, FontWeights.SemiBold));
            top.Children.Add(Label(DateTime.Now.Year == 2026 && DateTime.Now.Month >= 11
                ? "WINTER EDITION 2026  ❄" : T("Library tools", "Herramientas de biblioteca", "라이브러리 도구"),
                20, _foreground, FontWeights.Bold));
            top.Children.Add(Label(T("Two permanent tools. No AI, no internet, no automatic Mod changes.",
                "Dos herramientas permanentes. Sin IA, internet ni cambios automáticos a tus Mods.",
                "두 가지 영구 도구입니다. AI, 인터넷 및 자동 모드 변경 없이 사용합니다."),
                12, _muted, FontWeights.Normal));
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            top.Children.Add(tabs);
            _capsuleTab = Button("Library Time Capsule", () => SwitchTo(0));
            _guardianTab = Button("Mod Guardian", () => SwitchTo(1));
            _guardianTab.Margin = new Thickness(10, 0, 0, 0);
            tabs.Children.Add(_capsuleTab);
            tabs.Children.Add(_guardianTab);
            var fitButton = Button(T("Auto-fit", "Ajustar", "자동 맞춤"), ResetToAutoFit);
            fitButton.Margin = new Thickness(10, 0, 0, 0);
            tabs.Children.Add(fitButton);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _page.Children.Add(scroll);
            scroll.Content = _body;
            SwitchTo(_startTab);
        }

        private static bool TryReadWindowSize(out double width, out double height)
        {
            width = height = 0;
            try
            {
                if (!File.Exists(WindowSizeFile)) return false;
                using var doc = JsonDocument.Parse(File.ReadAllText(WindowSizeFile));
                if (!doc.RootElement.TryGetProperty("width", out var w) ||
                    !doc.RootElement.TryGetProperty("height", out var h) ||
                    !w.TryGetDouble(out width) || !h.TryGetDouble(out height)) return false;
                return double.IsFinite(width) && double.IsFinite(height) && width >= 400 && height >= 300;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
            {
                return false; // A damaged optional UI preference must not prevent app startup.
            }
        }

        private void SaveWindowSizeIfCustomized()
        {
            if (!_userSized || WindowState != WindowState.Normal ||
                !double.IsFinite(Width) || !double.IsFinite(Height)) return;
            try
            {
                var path = WindowSizeFile;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonSerializer.Serialize(new
                {
                    width = Math.Round(Width, 0), height = Math.Round(Height, 0)
                });
                // No scan history or folder paths are written to the UI preference.
                File.WriteAllText(path, json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Optional window sizing preference: never block application shutdown.
            }
        }

        private void ResetToAutoFit()
        {
            _userSized = false;
            try { File.Delete(WindowSizeFile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            QueueAutoFit();
        }

        private void QueueAutoFit()
        {
            if (!_layoutReady || _userSized || _autoFitQueued || WindowState != WindowState.Normal) return;
            _autoFitQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _autoFitQueued = false;
                FitContentHeight();
            }), DispatcherPriority.Background);
        }

        private void FitContentHeight()
        {
            if (!_layoutReady || _userSized || WindowState != WindowState.Normal) return;
            // Measure the complete scrollable content without clipping long histories.
            _body.Measure(new Size(Math.Max(320, _page.ActualWidth - 12), double.PositiveInfinity));
            var headerHeight = _hero.DesiredSize.Height;
            var bodyHeight = _body.DesiredSize.Height;
            if (!double.IsFinite(headerHeight) || !double.IsFinite(bodyHeight) || headerHeight < 80) return;
            // Includes title bar and the page margins (actual client area excluded).
            var frameAndMargins = Math.Max(42, ActualHeight - _page.ActualHeight);
            var maxHeight = Math.Max(MinHeight, Math.Min(760, SystemParameters.WorkArea.Height - 40));
            var desired = Math.Clamp(Math.Ceiling(headerHeight + bodyHeight + frameAndMargins + 8), MinHeight, maxHeight);
            if (Math.Abs(Height - desired) < 2) return;
            _suppressUserResize = true;
            Height = desired;
            Dispatcher.BeginInvoke(new Action(() => _suppressUserResize = false), DispatcherPriority.ContextIdle);
        }

        private static TextBlock Label(string value, int size, Brush color, FontWeight weight) => new()
        {
            Text = value, Foreground = color, FontSize = size, FontWeight = weight,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7)
        };
        private Button Button(string content, Action action)
        {
            var button = new Button
            {
                Content = content, Padding = new Thickness(14, 7, 14, 7), MinHeight = 36,
                Background = _surface, Foreground = _foreground,
                BorderBrush = ColorBrush(_winter ? "#8E535F" : "#405069"), BorderThickness = new Thickness(1),
                FontSize = 13, FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += (_, _) => action();
            return button;
        }
        private Border Card(params UIElement[] elements)
        {
            var stack = new StackPanel();
            foreach (var e in elements) stack.Children.Add(e);
            return new Border
            {
                Background = _surface, BorderBrush = ColorBrush(_winter ? "#77535A" : "#34435B"), BorderThickness = new Thickness(1),
                Padding = new Thickness(15), Margin = new Thickness(0, 4, 0, 10),
                CornerRadius = new CornerRadius(13), Child = stack
            };
        }
        private void SwitchTo(int index)
        {
            _body.Children.Clear();
            _capsuleTab.Background = index == 0 ? _accent : _surface;
            _guardianTab.Background = index == 1 ? _accent : _surface;
            if (index == 0) ShowCapsule(); else ShowGuardian();
            QueueAutoFit();
        }
        private bool ReadCurrentScan(out LastScanCacheService.Snapshot scan)
        {
            scan = new LastScanCacheService.Snapshot();
            if (_vm.IsScanning || _vm.ResultsAreCached || !_vm.HasLibrary ||
                string.IsNullOrWhiteSpace(_vm.ModsFolderPath) ||
                !LastScanCacheService.TryLoad(_vm.ModsFolderPath, out scan))
            {
                Info(T("Refresh Scan before using this feature. Restored scans are display-only.",
                    "Pulsa Actualizar análisis antes de usar esta función. Los escaneos recuperados son solo de consulta.",
                    "이 기능을 사용하기 전에 검사를 새로 고치세요. 복원된 검사는 보기 전용입니다."));
                return false;
            }
            return true;
        }
        private void Info(string text) => MessageBox.Show(this, text, "CC Suite Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        private void Error(Exception ex) => MessageBox.Show(this,
            T("Operation could not be completed: ", "No se pudo completar la operación: ", "작업을 완료할 수 없습니다: ") + ex.Message,
            "CC Suite Pro", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void ShowCapsule()
        {
            _body.Children.Add(Card(
                Label(T("Save a moment in your library's history", "Guarda un momento de la historia de tu biblioteca", "라이브러리의 순간을 기록하세요"),
                    19, _foreground, FontWeights.SemiBold),
                Label(T("Only totals, dates and category counts are saved locally; never filenames, paths or Mod bytes.",
                    "Solo se guardan estadísticas, fechas y categorías en tu perfil; nunca nombres, rutas ni archivos Mods.",
                    "통계, 날짜, 분류 개수만 로컬에 저장합니다. 파일 이름이나 경로, 모드 데이터는 저장하지 않습니다."), 12, _muted, FontWeights.Normal),
                Button(T("Create capsule from fresh scan", "Crear cápsula del escaneo actualizado", "새 검사로 캡슐 만들기"), CreateCapsule)));
            try
            {
                var capsules = WinterLibraryService.LoadCapsules();
                if (capsules.Count == 0)
                {
                    _body.Children.Add(Card(Label(T("No capsules yet. Start with a fresh scan.",
                        "Todavía no hay cápsulas. Comienza con un escaneo actualizado.", "아직 캡슐이 없습니다. 새 검사로 시작하세요."),
                        13, _muted, FontWeights.Normal)));
                    return;
                }
                var elements = new List<UIElement>
                {
                    Label(T("Saved time capsules", "Cápsulas guardadas", "저장된 타임 캡슐"), 18, _foreground, FontWeights.SemiBold)
                };
                foreach (var capsule in capsules.Take(10))
                {
                    string date = capsule.CreatedUtc.ToLocalTime().ToString("g");
                    string size = (capsule.Bytes / (1024.0 * 1024 * 1024)).ToString("0.##") + " GB";
                    elements.Add(Label($"{date}  ·  {capsule.Total:N0} Mods  ·  {size}  ·  {capsule.DuplicateGroups} " +
                        T("duplicate groups", "grupos duplicados", "중복 그룹"), 13, _foreground, FontWeights.Normal));
                }
                if (capsules.Count >= 2)
                {
                    var a = capsules[0]; var b = capsules[1];
                    elements.Add(Label(T("Change since previous capsule: ", "Cambio desde la cápsula anterior: ", "이전 캡슐과의 변화: ") +
                        $"{a.Total - b.Total:+#;-#;0} Mods, {(a.Bytes - b.Bytes) / (1024.0 * 1024 * 1024):+#.##;-#.##;0} GB",
                        13, _accent, FontWeights.SemiBold));
                }
                var share = Button(T("Export latest statistics card (PNG)", "Exportar tarjeta de estadísticas (PNG)", "최신 통계 카드 내보내기 (PNG)"),
                    () => ExportCard(capsules[0]));
                elements.Add(share);
                _body.Children.Add(Card(elements.ToArray()));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Newtonsoft.Json.JsonException)
            { Error(ex); }
        }

        private void CreateCapsule()
        {
            if (!ReadCurrentScan(out var scan)) return;
            try
            {
                WinterLibraryService.SaveCapsule(WinterLibraryService.MakeCapsule(scan));
                SwitchTo(0);
                Info(T("Time capsule saved in your Windows profile.", "Cápsula guardada en tu perfil de Windows.", "타임 캡슐이 Windows 프로필에 저장되었습니다."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or Newtonsoft.Json.JsonException)
            { Error(ex); }
        }

        private void ExportCard(WinterLibraryService.Capsule capsule)
        {
            var dialog = new SaveFileDialog
            {
                Title = T("Export private-safe card", "Exportar tarjeta sin datos privados", "개인 정보 없는 카드 내보내기"),
                Filter = "PNG (*.png)|*.png", FileName = "CCSuitePro_TimeCapsule_" + capsule.CreatedUtc.ToLocalTime().ToString("yyyyMMdd") + ".png"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var labels = new StackPanel { Background = ColorBrush(_winter ? "#2A1B23" : "#101B2B"), Width = 660, Margin = new Thickness(0) };
                labels.Children.Add(Label("CC SUITE PRO  ·  LIBRARY TIME CAPSULE", 18, _accent, FontWeights.Bold));
                labels.Children.Add(Label(capsule.CreatedUtc.ToLocalTime().ToString("D"), 24, Brushes.White, FontWeights.Bold));
                labels.Children.Add(Label($"{capsule.Total:N0} Mods    |    {capsule.Bytes / (1024.0 * 1024 * 1024):0.##} GB", 22, Brushes.White, FontWeights.SemiBold));
                labels.Children.Add(Label($"{capsule.Packages:N0} .package    ·    {capsule.Scripts:N0} .ts4script", 14, _muted, FontWeights.Normal));
                labels.Children.Add(Label($"{capsule.DuplicateGroups} " + T("duplicate groups", "grupos duplicados", "중복 그룹") +
                    $"    ·    {capsule.PotentialConflicts} " + T("potential conflicts", "posibles conflictos", "잠재 충돌"),
                    13, _muted, FontWeights.Normal));
                var outer = new Border { Width = 720, Background = ColorBrush(_winter ? "#2A1B23" : "#101B2B"), Padding = new Thickness(30), Child = labels };
                outer.Measure(new Size(720, 500));
                outer.Arrange(new Rect(new Point(0, 0), outer.DesiredSize));
                outer.UpdateLayout();
                int width = (int)Math.Ceiling(outer.RenderSize.Width);
                int height = (int)Math.Ceiling(outer.RenderSize.Height);
                var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(outer);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);
                Info(T("Statistics card saved. No paths or filenames were included.",
                    "Tarjeta guardada. No incluye rutas ni nombres de archivos.",
                    "통계 카드가 저장되었습니다. 경로나 파일 이름은 포함되지 않습니다."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { Error(ex); }
        }

        private void ShowGuardian()
        {
            _body.Children.Add(Card(
                Label(T("Spot what changed since your last reference scan", "Descubre qué cambió desde tu escaneo de referencia", "기준 검사 이후 변경된 파일을 확인하세요"),
                    19, _foreground, FontWeights.SemiBold),
                Label(T("Compares filenames and available scan hashes. Nothing is moved, deleted or repaired.",
                    "Compara nombres y hashes del escaneo. No mueve, elimina ni repara archivos.",
                    "검사 파일 이름과 해시를 비교합니다. 이동, 삭제 또는 복구하지 않습니다."), 12, _muted, FontWeights.Normal),
                Label(T("Guardian history is private and stays in your Windows profile; not in the public portable.",
                    "El registro es privado y permanece en tu perfil de Windows; no en el portable público.",
                    "Guardian 기록은 Windows 프로필에만 저장되며 배포판에 포함되지 않습니다."), 12, _muted, FontWeights.Normal)));
            _body.Children.Add(Card(
                Label(T("1. Save a reference scan", "1. Guardar escaneo de referencia", "1. 기준 검사 저장"), 16, _foreground, FontWeights.SemiBold),
                Button(T("Save / replace reference", "Guardar / reemplazar referencia", "기준 저장 / 교체"), SaveGuardian),
                Label(T("2. Refresh Scan, then compare", "2. Actualiza el escaneo y compara", "2. 검사를 새로 고치고 비교"), 16, _foreground, FontWeights.SemiBold),
                Button(T("Compare changes", "Comparar cambios", "변경 사항 비교"), CompareGuardian)));
        }

        private void SaveGuardian()
        {
            if (!ReadCurrentScan(out var scan)) return;
            var confirm = MessageBox.Show(this,
                T("Save this scan as the new reference? The previous reference for this Mods folder will be replaced.",
                    "¿Guardar este análisis como nueva referencia? Se reemplazará la referencia anterior de esta carpeta.",
                    "이 검사를 새 기준으로 저장할까요? 이 모드 폴더의 이전 기준을 교체합니다."),
                "Mod Guardian", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            try
            {
                WinterLibraryService.SaveBaseline(scan.FolderPath, WinterLibraryService.CreateBaseline(scan));
                Info(T("Reference saved. Refresh Scan after adding Mods, then compare.",
                    "Referencia guardada. Actualiza el análisis después de agregar Mods y compara.",
                    "기준을 저장했습니다. 모드를 추가한 후 검사를 새로 고치고 비교하세요."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or Newtonsoft.Json.JsonException)
            { Error(ex); }
        }

        private void CompareGuardian()
        {
            if (!ReadCurrentScan(out var scan)) return;
            try
            {
                var baseline = WinterLibraryService.LoadBaseline(scan.FolderPath);
                if (baseline == null)
                {
                    Info(T("Save a reference first.", "Primero guarda una referencia.", "먼저 기준을 저장하세요."));
                    return;
                }
                var current = WinterLibraryService.CreateBaseline(scan);
                var report = WinterLibraryService.Compare(baseline, current);
                _body.Children.Clear();
                var content = new List<UIElement>
                {
                    Label(T("Mod Guardian — comparison", "Mod Guardian — comparación", "Mod Guardian — 비교"), 20, _foreground, FontWeights.Bold),
                    Label(T("Reference: ", "Referencia: ", "기준: ") + baseline.ScannedUtc.ToLocalTime().ToString("g") +
                        T(" · Current scan: ", " · Escaneo actual: ", " · 현재 검사: ") + scan.ScannedAtUtc.ToLocalTime().ToString("g"),
                        12, _muted, FontWeights.Normal),
                    Label($"+{report.Added} " + T("new", "nuevos", "새 파일") + $"   {report.Changed} " +
                        T("changed", "modificados", "변경됨") + $"   {report.Missing} " +
                        T("not found", "no encontrados", "찾을 수 없음"), 20, _accent, FontWeights.SemiBold),
                    Label($"{report.CurrentDuplicateGroups} " + T("current duplicate groups", "grupos duplicados actuales", "현재 중복 그룹") +
                        $" ({report.DuplicateGroupDelta:+#;-#;0} " + T("since reference", "desde referencia", "기준 대비") + ")", 12, _muted, FontWeights.Normal),
                    Label(T("A missing path may mean the Mod was moved or renamed, not deleted.",
                        "Una ruta ausente puede significar que el Mod se movió o renombró, no que se borró.",
                        "경로가 없다는 것은 삭제가 아닌 이동 또는 이름 변경일 수 있습니다."), 12, _muted, FontWeights.Normal)
                };
                foreach (var item in report.Changes.Take(80))
                {
                    string name = item.Kind switch
                    {
                        "Added" => T("NEW", "NUEVO", "새 파일"),
                        "Changed" => T("CHANGED", "CAMBIADO", "변경"),
                        _ => T("NOT FOUND", "NO ENCONTRADO", "찾을 수 없음")
                    };
                    content.Add(Label(name + "   ·   " + item.Relative, 12, _foreground, FontWeights.Normal));
                }
                if (report.Changes.Count > 80)
                    content.Add(Label(T("Only the first 80 changes are displayed.", "Solo se muestran los primeros 80 cambios.",
                        "처음 80개 변경 사항만 표시합니다."), 12, _muted, FontWeights.Normal));
                content.Add(Button(T("Back", "Volver", "뒤로"), () => SwitchTo(1)));
                _body.Children.Add(Card(content.ToArray()));
                QueueAutoFit();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or Newtonsoft.Json.JsonException)
            { Error(ex); }
        }
    }
}
