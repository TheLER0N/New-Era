using System.Windows;
using System.Windows.Controls;

namespace MainApp;

public partial class RolePickWindow : Window
{
    public string SelectedRole { get; private set; } = "coder";
    public RolePickWindow() => InitializeComponent();
    private void Pick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string r) SelectedRole = r;
        DialogResult = true;
    }
}