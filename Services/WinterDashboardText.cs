using System;
using System.ComponentModel;

namespace CCSuitePro.Services
{
    // Texts for the Winter 29 Dashboard card. Keeps the language JSON files untouched.
    // LanguageService notifies Item[] and CurrentLanguage on a locale switch.
    public sealed class WinterDashboardText : INotifyPropertyChanged
    {
        private static readonly WinterDashboardText _instance = new WinterDashboardText();
        public static WinterDashboardText Instance => _instance;
        public event PropertyChangedEventHandler? PropertyChanged;

        private WinterDashboardText()
        {
            LanguageService.Instance.PropertyChanged += LanguageChanged;
        }

        private void LanguageChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LanguageService.CurrentLanguage) && e.PropertyName != "Item[]")
                return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        private static string Choose(string en, string es, string ko) =>
            LanguageService.Instance.CurrentLanguage switch
            {
                "es" => es,
                "ko" => ko,
                _ => en
            };

        public string Title => Choose("Library tools", "Herramientas de biblioteca", "라이브러리 도구");
        public string Summary => Choose(
            "Explore your history and compare scans without changing Mods.",
            "Consulta el historial y compara análisis sin modificar tus Mods.",
            "모드를 변경하지 않고 기록을 확인하고 스캔 결과를 비교하세요.");
        public string CapsuleHint => Choose(
            "Review local snapshots of library statistics",
            "Revisa las estadísticas históricas guardadas localmente",
            "로컬에 저장된 라이브러리 통계 기록을 확인합니다");
        public string GuardianHint => Choose(
            "Compare files with a saved reference scan",
            "Compara archivos con un análisis de referencia guardado",
            "저장된 기준 스캔과 파일을 비교합니다");
        public string AIHint => Choose(
            "Experimental AI remains disabled in this release.",
            "La IA experimental sigue desactivada en esta versión.",
            "이 버전에서는 실험적 AI가 비활성화되어 있습니다.");
    }
}
