using System.Windows.Controls;
using Inmobiliaria.Presentation.Indices;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The Índices screen, bound to <see cref="IndicesViewModel"/>. No logic lives here: the only code reads the list each time
/// the screen is shown, so it reflects what other PCs wrote meanwhile.
/// </summary>
public partial class IndicesView : UserControl
{
    public IndicesView()
    {
        InitializeComponent();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is IndicesViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };
    }
}
