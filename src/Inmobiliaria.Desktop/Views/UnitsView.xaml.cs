using System.Windows.Controls;
using Inmobiliaria.Presentation.Units;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The Propiedades screen, bound to <see cref="UnitsViewModel"/>. No logic lives here: the only code reads the list
/// each time the screen is shown, so it reflects what other PCs wrote meanwhile.
/// </summary>
public partial class UnitsView : UserControl
{
    public UnitsView()
    {
        InitializeComponent();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is UnitsViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };
    }
}
