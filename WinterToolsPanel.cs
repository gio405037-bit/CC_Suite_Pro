using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CCSuitePro.Services;
using CCSuitePro.ViewModels;
using Microsoft.Win32;

namespace CCSuitePro
{
    /// <summary>
    /// Embedded presentation for the existing read-only Time Capsule / Guardian tools.
    /// No scan, persistence or comparison algorithms are changed here.
    /// </summary>
    public sealed class WinterToolsPanel : UserControl
    {
        private readonly MainViewModel _vm;
        private readonly Action<int> _navigate;
        private readonly Brush _surface;
        private readonly Brush _foreground = ColorBrush("#F3F7FD");
        private readonly Brush _muted = ColorBrush("#ABB9CE");
        private readonly Brush _accent;
        private readonly bool _winter;
        private readonly StackPanel _body = new() { Margin = new Thickness(4, 0, 4, 12) };
        private readonly Button _capsuleTab;
        private readonly Button _guardianTab;

        private static Brush ColorBrush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
        private static Color ColorFrom(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
        private static string T(string en, string es, string ko) => LanguageService.Instance.CurrentLanguage switch
        {
            "es" => es, "ko" => ko, _ => en
        };

        private Window OwnerWindow => Window.GetWindow(this) ?? Application.Current.MainWindow;

        public WinterToolsPanel(MainViewModel vm, int page, Action<int> navigate)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
            bool premium = ThemeManager.Current == ThemeVariant.NegroPremium;
            _winter = ThemeManager.Current == ThemeVariant.WinterEdition;
            _accent = ColorBrush(_winter ? "#D6757F" : "#4D90FF");
            _surface = ColorBrush(_winter ? "#2B2229" : premium ? "#17191E" : "#15233A");
            Foreground = _foreground;
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;

            // El encabezado principal ya existe en MainWindow: no repetirlo aquí.
            // Mantener una sola región desplazable y controles de ancho natural.
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = true
            };
            Content = scroll;
            var layout = new StackPanel { Margin = new Thickness(4, 0, 8, 16) };
            scroll.Content = layout;

            var header = new StackPanel();
            var ribbon = new Border
            {
                Padding = new Thickness(15, 10, 15, 10),
                Margin = new Thickness(0, 0, 0, 10),
                CornerRadius = new CornerRadius(12),
                BorderBrush = ColorBrush(_winter ? "#794A55" : "#34435B"),
                BorderThickness = new Thickness(1),
                Background = _winter
                    ? new LinearGradientBrush(ColorFrom("#38252C"), ColorFrom("#1D191E"), 12)
                    : ColorBrush(premium ? "#17191E" : "#101A2A"),
                Child = header
            };
            layout.Children.Add(ribbon);
            header.Children.Add(Label(_winter
                ? "✧  " + T("WINTER COLLECTION 2026  ·  LIBRARY TOOLS", "COLECCIÓN WINTER 2026  ·  BIBLIOTECA", "WINTER 컬렉션 2026  ·  라이브러리")
                : T("LIBRARY TOOLS", "HERRAMIENTAS DE BIBLIOTECA", "라이브러리 도구"),
                11, _winter ? ColorBrush("#F1BAC3") : _accent, FontWeights.SemiBold));
            var tabs = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) }; // WINTER36: navigation wraps in compact windows
            header.Children.Add(tabs);
            _capsuleTab = Button(T("Time Capsule", "Cápsula del tiempo", "타임 캡슐"), () => _navigate(0));
            _guardianTab = Button("Mod Guardian", () => _navigate(1));
            _guardianTab.Margin = new Thickness(9, 0, 0, 0);
            tabs.Children.Add(_capsuleTab);
            tabs.Children.Add(_guardianTab);
            layout.Children.Add(_body);
            SwitchTo(page);
        }

        private static TextBlock Label(string value, int size, Brush color, FontWeight weight) => new()
        {
            Text = value, Foreground = color, FontSize = size, FontWeight = weight,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7)
        };

        private Button Button(string content, Action action, bool primary = false)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                Padding = new Thickness(15, 7, 15, 7), MinHeight = 36, // WINTER36: long translations wrap
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = primary ? _accent : _surface,
                Foreground = primary ? ColorBrush("#151119") : _foreground,
                BorderBrush = ColorBrush(_winter ? "#8E535F" : "#405069"),
                BorderThickness = new Thickness(1), FontSize = 13,
                FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += (_, _) => action();
            return button;
        }

        private Border Card(params UIElement[] elements)
        {
            var stack = new StackPanel();
            foreach (var element in elements) stack.Children.Add(element);
            return new Border
            {
                Background = _surface,
                BorderBrush = ColorBrush(_winter ? "#77535A" : "#34435B"),
                BorderThickness = new Thickness(1), Padding = new Thickness(16),
                Margin = new Thickness(0, 3, 0, 10),
                CornerRadius = new CornerRadius(13), Child = stack
            };
        }

        // Reorganiza las tarjetas en una, dos o tres columnas al cambiar la ventana.
        // Solo cambia tamaños visuales; no observa ni modifica la carpeta Mods.
        private WrapPanel Tiles(int maximumColumns, params Border[] cards)
        {
            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var card in cards)
            {
                card.Margin = new Thickness(0, 0, 10, 10);
                wrap.Children.Add(card);
            }
            void Resize(double width)
            {
                if (width <= 0) return;
                int columns = width >= 810 ? 3 : width >= 510 ? 2 : 1;
                columns = Math.Max(1, Math.Min(Math.Min(columns, maximumColumns), cards.Length));
                // WINTER36: never force a 160px tile into a narrower viewport.
                double tileWidth = Math.Max(0, Math.Floor((width - columns * 10 - 2) / columns));
                foreach (var card in cards) card.Width = tileWidth;
            }
            wrap.SizeChanged += (_, e) => Resize(e.NewSize.Width);
            wrap.Loaded += (_, _) => Resize(wrap.ActualWidth);
            return wrap;
        }

        private Border Metric(string title, string number, string explanation)
        {
            return Card(
                Label(title, 12, _muted, FontWeights.SemiBold),
                Label(number, 23, _foreground, FontWeights.Bold),
                Label(explanation, 12, _muted, FontWeights.Normal));
        }

        private void SwitchTo(int index)
        {
            _body.Children.Clear();
            _capsuleTab.Background = index == 0 ? _accent : _surface;
            _guardianTab.Background = index == 1 ? _accent : _surface;
            _capsuleTab.Foreground = index == 0 ? ColorBrush("#151119") : _foreground;
            _guardianTab.Foreground = index == 1 ? ColorBrush("#151119") : _foreground;
            if (index == 0) ShowCapsule(); else ShowGuardian();
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
        private void Info(string text) => MessageBox.Show(OwnerWindow, text, "CC Suite Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        private void Error(Exception ex) => MessageBox.Show(OwnerWindow,
            T("Operation could not be completed: ", "No se pudo completar la operación: ", "작업을 완료할 수 없습니다: ") + ex.Message,
            "CC Suite Pro", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void ShowCapsule()
        {
            _body.Children.Add(Card(
                Label(T("Save a moment in your library's history", "Guarda un momento de tu biblioteca", "라이브러리의 순간을 기록하세요"),
                    19, _foreground, FontWeights.SemiBold),
                Label(T("Capture statistics to compare your library over time. No filenames, paths or Mod files are stored.",
                    "Conserva estadísticas para comparar épocas. No se guardan nombres, rutas ni archivos Mods.",
                    "시간별 통계를 비교합니다. 파일 이름, 경로 또는 모드 파일은 저장되지 않습니다."),
                    12, _muted, FontWeights.Normal),
                Button(T("Create time capsule", "Crear cápsula del tiempo", "타임 캡슐 만들기"), CreateCapsule, true)));
            try
            {
                var capsules = WinterLibraryService.LoadCapsules();
                _body.Children.Add(Tiles(3,
                    Metric(T("CAPSULES", "CÁPSULAS", "캡슐"), capsules.Count.ToString("N0"),
                        T("Saved snapshots", "Registros históricos", "저장된 기록")),
                    Metric(T("LATEST", "ÚLTIMA CAPTURA", "최근 기록"),
                        capsules.Count == 0 ? "—" : capsules[0].CreatedUtc.ToLocalTime().ToString("d"),
                        T("Local date", "Fecha local", "현지 날짜")),
                    Metric(T("RECORDED MODS", "MODS REGISTRADOS", "기록된 모드"),
                        capsules.Count == 0 ? "—" : capsules[0].Total.ToString("N0"),
                        T("Latest capsule", "Última cápsula", "최신 캡슐"))));
                if (capsules.Count == 0)
                {
                    _body.Children.Add(Card(
                        Label("✧  " + T("Your story starts here", "Tu historia comienza aquí", "기록은 여기에서 시작됩니다"),
                            18, _accent, FontWeights.SemiBold),
                        Label(T("Run Refresh Scan above, then create your first capsule to preserve a private statistical snapshot.",
                            "Pulsa Actualizar análisis en la parte superior y crea tu primera cápsula estadística privada.",
                            "위에서 검사를 새로 고치고 첫 번째 비공개 통계 캡슐을 만들어 보세요."),
                            13, _muted, FontWeights.Normal)));
                    return;
                }
                if (capsules.Count > 1)
                {
                    var a = capsules[0]; var b = capsules[1];
                    _body.Children.Add(Card(
                        Label(T("SINCE THE PREVIOUS CAPSULE", "DESDE LA CÁPSULA ANTERIOR", "이전 캡슐 이후"),
                            12, _muted, FontWeights.SemiBold),
                        Label($"{a.Total - b.Total:+#;-#;0} Mods    ·    {(a.Bytes - b.Bytes) / (1024.0 * 1024 * 1024):+#.##;-#.##;0} GB",
                            19, _accent, FontWeights.SemiBold)));
                }
                _body.Children.Add(Card(
                    Label(T("Capsule history", "Historial de cápsulas", "캡슐 기록"),
                        18, _foreground, FontWeights.SemiBold),
                    Label(T("Only local statistics are displayed. Most recent first.",
                        "Solo se muestran estadísticas locales, de la más reciente a la anterior.",
                        "로컬 통계만 최신순으로 표시합니다."), 12, _muted, FontWeights.Normal)));
                var history = capsules.Take(10).Select(capsule => Card(
                    Label(capsule.CreatedUtc.ToLocalTime().ToString("g"), 14, _foreground, FontWeights.SemiBold),
                    Label($"{capsule.Total:N0} Mods  ·  {capsule.Bytes / (1024.0 * 1024 * 1024):0.##} GB",
                        13, _accent, FontWeights.SemiBold),
                    Label($"{capsule.Packages:N0} .package  ·  {capsule.Scripts:N0} .ts4script  ·  {capsule.DuplicateGroups} " +
                        T("duplicate groups", "grupos duplicados", "중복 그룹"), 12, _muted, FontWeights.Normal)))
                    .ToArray();
                _body.Children.Add(Tiles(2, history));
                _body.Children.Add(Card(Button(T("Export latest statistics card (PNG)",
                    "Exportar última tarjeta estadística (PNG)", "최신 통계 카드 내보내기 (PNG)"),
                    () => ExportCard(capsules[0]))));
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
            if (dialog.ShowDialog(OwnerWindow) != true) return;
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
                Label(T("Review changes without touching your Mods", "Revisa cambios sin tocar tus Mods", "모드 파일을 변경하지 않고 차이를 확인하세요"),
                    19, _foreground, FontWeights.SemiBold),
                Label(T("Compare filenames and available scan hashes. Nothing is moved, deleted or repaired.",
                    "Compara nombres y hashes disponibles. No se mueve, elimina ni repara ningún archivo.",
                    "파일 이름과 사용 가능한 해시를 비교합니다. 파일 이동, 삭제, 복구는 없습니다."),
                    12, _muted, FontWeights.Normal),
                Label(T("Private reference data stays in your Windows profile, not in the public portable.",
                    "La referencia privada permanece en tu perfil de Windows, nunca en el portable público.",
                    "개인 기준 데이터는 공개 포터블이 아닌 Windows 프로필에만 저장됩니다."),
                    12, _muted, FontWeights.Normal)));
            string state = T("No reference saved", "Sin referencia guardada", "저장된 기준 없음");
            if (_vm.HasLibrary && !string.IsNullOrWhiteSpace(_vm.ModsFolderPath))
            {
                try
                {
                    var baseline = WinterLibraryService.LoadBaseline(_vm.ModsFolderPath);
                    if (baseline != null)
                        state = T("Reference: ", "Referencia: ", "기준: ") + baseline.ScannedUtc.ToLocalTime().ToString("g");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Newtonsoft.Json.JsonException)
                {
                    Error(ex);
                }
            }
            _body.Children.Add(Card(
                Label(T("REFERENCE STATUS", "ESTADO DE REFERENCIA", "기준 상태"), 12, _muted, FontWeights.SemiBold),
                Label(state, 15, _accent, FontWeights.SemiBold)));
            _body.Children.Add(Tiles(2,
                Card(
                    Label(T("STEP 1", "PASO 1", "1단계"), 12, _accent, FontWeights.SemiBold),
                    Label(T("Save your reference", "Guardar referencia", "기준 저장"), 17, _foreground, FontWeights.SemiBold),
                    Label(T("Run a fresh scan and save the starting point for later comparisons.",
                        "Haz un análisis actualizado y guarda el punto de partida.",
                        "새 검사를 실행하고 나중에 비교할 시작점을 저장하세요."), 12, _muted, FontWeights.Normal),
                    Button(T("Save / replace reference", "Guardar / reemplazar referencia", "기준 저장 / 교체"), SaveGuardian, true)),
                Card(
                    Label(T("STEP 2", "PASO 2", "2단계"), 12, _accent, FontWeights.SemiBold),
                    Label(T("Compare changes", "Comparar cambios", "변경 사항 비교"), 17, _foreground, FontWeights.SemiBold),
                    Label(T("After changes to your library, refresh the scan using the top toolbar.",
                        "Después de cambiar tu biblioteca, actualiza el análisis desde la barra superior.",
                        "라이브러리 변경 후 위쪽 도구 모음에서 검사를 갱신하세요."), 12, _muted, FontWeights.Normal),
                    Button(T("View comparison", "Ver comparación", "비교 보기"), CompareGuardian))));
        }

        private void SaveGuardian()
        {
            if (!ReadCurrentScan(out var scan)) return;
            var confirm = MessageBox.Show(OwnerWindow,
                T("Save this scan as the new reference? The previous reference for this Mods folder will be replaced.",
                    "¿Guardar este análisis como nueva referencia? Se reemplazará la referencia anterior de esta carpeta.",
                    "이 검사를 새 기준으로 저장할까요? 이 모드 폴더의 이전 기준을 교체합니다."),
                "Mod Guardian", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            try
            {
                WinterLibraryService.SaveBaseline(scan.FolderPath, WinterLibraryService.CreateBaseline(scan));
                SwitchTo(1); // WINTER36: immediately refresh saved-reference status
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
                _body.Children.Add(Card(
                    Label(T("Comparison results", "Resultados de la comparación", "비교 결과"), 20, _foreground, FontWeights.Bold),
                    Label(T("Reference: ", "Referencia: ", "기준: ") + baseline.ScannedUtc.ToLocalTime().ToString("g") +
                        T("   ·   Current scan: ", "   ·   Escaneo actual: ", "   ·   현재 검사: ") +
                        scan.ScannedAtUtc.ToLocalTime().ToString("g"), 12, _muted, FontWeights.Normal),
                    Label(T("Read-only results. A missing Mod may have been moved or renamed.",
                        "Resultados de solo lectura. Un Mod ausente pudo moverse o cambiar de nombre.",
                        "읽기 전용 결과입니다. 누락된 모드는 이동되거나 이름이 변경되었을 수 있습니다."),
                        12, _muted, FontWeights.Normal)));
                _body.Children.Add(Tiles(3,
                    Metric(T("NEW", "NUEVOS", "새 파일"), $"+{report.Added:N0}",
                        T("Since reference", "Desde la referencia", "기준 이후")),
                    Metric(T("MODIFIED", "MODIFICADOS", "변경됨"), report.Changed.ToString("N0"),
                        T("Detected changes", "Cambios detectados", "감지된 변경")),
                    Metric(T("NOT FOUND", "AUSENTES", "찾을 수 없음"), report.Missing.ToString("N0"),
                        T("May be moved", "Pueden haberse movido", "이동되었을 수 있음"))));
                var content = new List<UIElement>
                {
                    Label(T("Change details", "Detalle de cambios", "변경 내역"), 18, _foreground, FontWeights.SemiBold),
                    Label($"{report.CurrentDuplicateGroups} " + T("current duplicate groups", "grupos duplicados actuales", "현재 중복 그룹") +
                        $" ({report.DuplicateGroupDelta:+#;-#;0} " + T("since reference", "desde la referencia", "기준 대비") + ")",
                        12, _muted, FontWeights.Normal)
                };
                if (report.Changes.Count == 0)
                    content.Add(Label(T("No changes were detected.", "No se detectaron cambios.", "변경 사항이 감지되지 않았습니다."),
                        13, _accent, FontWeights.SemiBold));
                foreach (var item in report.Changes.Take(80))
                {
                    string kind = item.Kind switch
                    {
                        "Added" => T("NEW", "NUEVO", "새 파일"),
                        "Changed" => T("MODIFIED", "MODIFICADO", "변경"),
                        _ => T("NOT FOUND", "AUSENTE", "찾을 수 없음")
                    };
                    // Los nombres son visibles únicamente aquí, nunca en tarjetas exportadas.
                    content.Add(Label(kind + "   ·   " + item.Relative, 12, _foreground, FontWeights.Normal));
                }
                if (report.Changes.Count > 80)
                    content.Add(Label(T("Showing only the first 80 changes.",
                        "Solo se muestran los primeros 80 cambios.", "처음 80개 변경 사항만 표시합니다."),
                        12, _muted, FontWeights.Normal));
                _body.Children.Add(Card(content.ToArray()));
                _body.Children.Add(Card(Button(T("Back to Mod Guardian", "Volver a Mod Guardian", "Mod Guardian로 돌아가기"),
                    () => SwitchTo(1))));
                }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or Newtonsoft.Json.JsonException)
            { Error(ex); }
        }
    }
}
