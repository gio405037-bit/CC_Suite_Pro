using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CCSuitePro.Services;
using CCSuitePro.ViewModels;

namespace CCSuitePro
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        
        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            
            // Aplicar idioma después de cargar
            this.Loaded += (s, e) => ApplyLanguage();
        }
        
        private void ApplyLanguage()
        {
            var lang = LanguageService.Instance;
            
            // Buscar todos los TextBlock en toda la ventana
            foreach (var tb in FindAllTextBlocks(this))
            {
                if (tb.Tag is string key && !string.IsNullOrEmpty(key))
                {
                    tb.Text = lang[key];
                }
            }
        }
        
        private IEnumerable<TextBlock> FindAllTextBlocks(DependencyObject parent)
        {
            if (parent == null) yield break;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is TextBlock tb)
                {
                    yield return tb;
                }
                else if (child is ContentControl cc && cc.Content is FrameworkElement fe)
                {
                    foreach (var result in FindAllTextBlocks(fe))
                        yield return result;
                }
                
                foreach (var result in FindAllTextBlocks(child))
                    yield return result;
            }
        }
        
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseWindow(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        
        private void SwitchTab(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tagStr && int.TryParse(tagStr, out int index))
                MainTabs.SelectedIndex = index;
        }
    }
}
