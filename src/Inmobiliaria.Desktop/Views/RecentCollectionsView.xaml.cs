using System.Windows.Controls;
using Inmobiliaria.Presentation.Recibos;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The recent collections, bound to <see cref="RecentCollectionsViewModel"/>. No logic lives here: the only code
/// reads the list each time the screen is shown, so a payment recorded meanwhile is there on arrival.
/// </summary>
public partial class RecentCollectionsView : UserControl
{
    public RecentCollectionsView()
    {
        InitializeComponent();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is RecentCollectionsViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };
    }
}
