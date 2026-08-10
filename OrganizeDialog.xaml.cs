using System.Windows;
using CCSuitePro.ViewModels;

namespace CCSuitePro
{
    public partial class OrganizeDialog : Window
    {
        private readonly MainViewModel _viewModel;
        
        public OrganizeDialog(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
        }
        
        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.PreviewOrganizeCommand.Execute(null);
        }
        
        private void Organize_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ExecuteOrganizeCommand.Execute(null);
            DialogResult = true;
            Close();
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
