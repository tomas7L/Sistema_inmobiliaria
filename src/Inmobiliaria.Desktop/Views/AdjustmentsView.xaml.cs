using System.Windows.Controls;
using Inmobiliaria.Presentation.Adjustments;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The Ajustes screen, bound to <see cref="AdjustmentsViewModel"/>. No logic lives here: the only code reads the list each time
/// the screen is shown, so it reflects what other PCs confirmed and the index values loaded meanwhile.
/// </summary>
public partial class AdjustmentsView : UserControl
{
    public AdjustmentsView()
    {
        InitializeComponent();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is AdjustmentsViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };
    }
}
