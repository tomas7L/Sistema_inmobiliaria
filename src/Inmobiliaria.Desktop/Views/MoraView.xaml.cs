using System.Windows.Controls;
using Inmobiliaria.Presentation.Mora;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The Mora screen, bound to <see cref="MoraViewModel"/>. No logic lives here: the only code reads the list each time the
/// screen is shown (mora spec Decision 13: on opening and when asked, never on a timer).
/// </summary>
public partial class MoraView : UserControl
{
    public MoraView()
    {
        InitializeComponent();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is MoraViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };
    }
}
