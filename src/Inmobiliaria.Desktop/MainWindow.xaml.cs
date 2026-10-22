using System.Windows;
using Inmobiliaria.Presentation.Shell;

namespace Inmobiliaria.Desktop;

/// <summary>
/// The main window: who is working and the screens that role is offered, bound to
/// <see cref="ShellViewModel"/>. No logic lives here; the screens arrive with their slices.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();

        DataContext = viewModel;
    }
}
