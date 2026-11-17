using System.Windows.Controls;
using Inmobiliaria.Presentation.Contracts;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The form of a new contract, bound to <see cref="ContractFormViewModel"/>. No logic lives here: the list that hosts it reads
/// the catalog, the people and the units when it opens it.
/// </summary>
public partial class ContractFormView : UserControl
{
    public ContractFormView()
    {
        InitializeComponent();
    }
}
