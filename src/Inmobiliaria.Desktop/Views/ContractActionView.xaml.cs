using System.Windows.Controls;
using Inmobiliaria.Presentation.Contracts;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The contract's actions with their own screen ("Registrar aviso", "Deshacer aviso"), bound to the open contract's
/// <see cref="ContractDetailViewModel"/>. No logic lives here: the list that hosts the detail shows this in its place while an
/// action is open.
/// </summary>
public partial class ContractActionView : UserControl
{
    public ContractActionView()
    {
        InitializeComponent();
    }
}
