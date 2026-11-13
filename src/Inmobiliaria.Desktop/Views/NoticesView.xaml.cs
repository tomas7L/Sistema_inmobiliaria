using System.Windows;
using System.Windows.Controls;
using Inmobiliaria.Presentation.Notices;

namespace Inmobiliaria.Desktop.Views;

/// <summary>
/// The Avisos screen, bound to <see cref="NoticesViewModel"/>. The only code reads the list each time the screen is shown,
/// and relays the app password, because <see cref="PasswordBox"/> deliberately does not bind (as in the login window).
/// </summary>
public partial class NoticesView : UserControl
{
    public NoticesView()
    {
        InitializeComponent();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && DataContext is NoticesViewModel { LoadCommand: var load } && load.CanExecute(null))
            {
                load.Execute(null);
            }
        };

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is NoticesViewModel { MailboxSetup: { } oldSetup })
            {
                oldSetup.Saved -= OnMailboxSaved;
            }

            if (e.NewValue is NoticesViewModel { MailboxSetup: { } newSetup })
            {
                newSetup.Saved += OnMailboxSaved;
            }
        };
    }

    private void AppPasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is NoticesViewModel { MailboxSetup: { } setup })
        {
            setup.AppPassword = ((PasswordBox)sender).Password;
        }
    }

    /// <summary>The view-model empties its copy once saved; the box is emptied with it.</summary>
    private void OnMailboxSaved(object? sender, EventArgs e) => AppPasswordInput.Clear();
}
