using System.Windows.Controls;
using Inmobiliaria.Presentation.Contracts;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// "Registrar aviso", bound to the open contract's <see cref="ContractDetailViewModel"/>. No logic lives here: the list that
/// hosts the detail shows this in its place while the action is open.
/// </summary>
public partial class ContractNoticeView : UserControl
{
    public ContractNoticeView()
    {
        InitializeComponent();
    }
}
