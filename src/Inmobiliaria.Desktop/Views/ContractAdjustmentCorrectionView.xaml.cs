using System.Windows.Controls;
using Inmobiliaria.Presentation.Contracts;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The correction of the contract's latest adjustment ("Corregir ajuste"), bound to the open contract's
/// <see cref="ContractDetailViewModel"/> and its <see cref="ContractAdjustmentsViewModel"/>. No logic lives here: the list that
/// hosts the detail shows this in its place while the correction is open.
/// </summary>
public partial class ContractAdjustmentCorrectionView : UserControl
{
    public ContractAdjustmentCorrectionView()
    {
        InitializeComponent();
    }
}
