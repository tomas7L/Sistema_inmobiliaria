using System.Windows.Controls;
using Inmobiliaria.Presentation.Users;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The Usuarios screen, bound to <see cref="UsersViewModel"/>. No logic lives here: the only code reads the list
/// each time the screen is shown, so a change another Admin made meanwhile is visible on arrival.
/// </summary>
public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is UsersViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };
    }
}
