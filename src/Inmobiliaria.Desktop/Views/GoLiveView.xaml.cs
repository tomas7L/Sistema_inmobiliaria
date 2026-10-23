using System.Windows;
using System.Windows.Controls;
using Inmobiliaria.Presentation.GoLive;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The go-live screen, bound to <see cref="GoLiveViewModel"/>. No logic lives here: the only code reads
/// the screen's state each time it is shown, so a lock that arrived meanwhile is visible on arrival.
/// </summary>
public partial class GoLiveView : UserControl
{
    public GoLiveView()
    {
        InitializeComponent();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is GoLiveViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };
    }
}
