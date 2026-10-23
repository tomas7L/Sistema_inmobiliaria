using System.Windows;
using Inmobiliaria.Presentation.GoLive;
using Inmobiliaria.Presentation.Shell;

namespace Inmobiliaria.Desktop;

/// <summary>
/// The main window: who is working and the screens that role is offered, bound to
/// <see cref="ShellViewModel"/>. No logic lives here; the screens arrive with their slices, each given
/// its view-model by the composition root. A screen the role is not offered gets none and is never shown.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel, GoLiveViewModel? goLive)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();

        DataContext = viewModel;
        GoLiveScreen.DataContext = goLive;
    }
}
