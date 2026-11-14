using System.Windows.Controls;
using Inmobiliaria.Presentation.Parties;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The Personas screen, bound to <see cref="PartiesViewModel"/>. No logic lives here: the only code reads the list
/// each time the screen is shown, so a person another operator saved meanwhile is listed on arrival.
/// </summary>
public partial class PartiesView : UserControl
{
    public PartiesView()
    {
        InitializeComponent();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is PartiesViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };
    }
}
