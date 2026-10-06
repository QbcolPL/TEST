using System.Windows;
namespace FieldTakHub.Builder;
public partial class PasswordPrompt:Window
{
    public string Password => PasswordBox.Password;
    public PasswordPrompt(){InitializeComponent();Loaded+=(_,_)=>PasswordBox.Focus();}
    private void Ok_Click(object sender,RoutedEventArgs e){DialogResult=true;}
    private void Cancel_Click(object sender,RoutedEventArgs e){DialogResult=false;}
}
