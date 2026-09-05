using System.Windows.Input;
using System.Windows.Controls;

namespace DevEnvManager.Views;

public partial class StoreView : UserControl
{
    public StoreView()
    {
        InitializeComponent();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ViewModels.StoreViewModel vm)
        {
            if (vm.SearchCommand.CanExecute(null))
                vm.SearchCommand.Execute(null);
        }
    }
}
