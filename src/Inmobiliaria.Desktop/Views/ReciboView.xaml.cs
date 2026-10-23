using System.Windows.Controls;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// A payment's two recibos, bound to <see cref="Inmobiliaria.Presentation.Recibos.ReciboViewModel"/>. No logic
/// lives here.
/// </summary>
public partial class ReciboView : UserControl
{
    public ReciboView()
    {
        InitializeComponent();
    }
}
