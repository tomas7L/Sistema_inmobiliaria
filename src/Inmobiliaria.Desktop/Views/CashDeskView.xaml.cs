using System.Windows.Controls;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The cash desk, bound to <see cref="Inmobiliaria.Presentation.CashDesk.CashDeskViewModel"/>: the account search
/// beside the payment. No logic lives here; money fields bind on lost focus, so a preview follows a finished
/// value rather than every keystroke (design D23).
/// </summary>
public partial class CashDeskView : UserControl
{
    public CashDeskView()
    {
        InitializeComponent();
    }
}
