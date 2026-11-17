using System.Windows.Controls;
using Inmobiliaria.Presentation.Contracts;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The Contratos screen, bound to <see cref="ContractsViewModel"/>. No logic lives here: the only code reads the list
/// each time the screen is shown, so it reflects what other PCs wrote meanwhile.
/// </summary>
public partial class ContractsView : UserControl
{
    public ContractsView()
    {
        InitializeComponent();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is ContractsViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };
    }
}
