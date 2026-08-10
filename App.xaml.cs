using System.Windows;

namespace CCSuitePro
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Cargar configuración guardada
            Services.SettingsService.Load();
            
            // Aplicar idioma guardado
            Services.LanguageService.Instance.LoadLanguage(
                Services.SettingsService.Current.Language
            );
        }
    }
}
