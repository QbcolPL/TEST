using System.Windows;

namespace FieldTakHub.Builder;

public partial class NewProjectDialog : Window
{
    public NewProjectDialog() => InitializeComponent();
    public string ProjectName => ProjectNameBox.Text.Trim();
    public string PackageId => PackageIdBox.Text.Trim();
    public string PackageVersion => PackageVersionBox.Text.Trim();

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            MessageBox.Show(Services.LocalizationService.Text("ProjectNameRequired", "Enter a project name."), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(PackageId))
        {
            MessageBox.Show(Services.LocalizationService.Text("PackageIdRequired", "Enter a package ID."), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
