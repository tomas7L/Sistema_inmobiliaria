using System.Windows.Controls;
using Inmobiliaria.Presentation.Contracts;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// One contract's detail, bound to <see cref="ContractDetailViewModel"/>. No logic lives here: the list that hosts it reads
/// the contract when it opens it.
/// </summary>
public partial class ContractDetailView : UserControl
{
    public ContractDetailView()
    {
        InitializeComponent();
    }
}
