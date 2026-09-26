using System.Windows;
using System.Windows.Controls;

namespace Inmobiliaria.Desktop;

/// <summary>
/// Used both forced (right after a login whose <c>MustChangePassword</c> is pending) and
/// voluntary (design Decision 10's table) — <see cref="ChangePasswordViewModel.IsForced"/> is
/// the only thing that differs. Relays the ViewModel's two terminal events into
/// <see cref="Window.DialogResult"/>: <see langword="true"/> on success,
/// <see langword="false"/> on an explicit cancel. Closing any other way (the window chrome)
/// leaves <see cref="Window.DialogResult"/> unset, which <c>ShowDialog()</c> reports as
/// <see langword="null"/> — the bootstrap treats anything other than <see langword="true"/> as
/// "not succeeded" (design Decision 10: "closed without success ⇒ dispose the session and
/// return to LoginWindow"), so an unset result is never mistaken for success.
/// </summary>
public partial class ChangePasswordWindow : Window
{
    private readonly ChangePasswordViewModel _viewModel;

    public ChangePasswordWindow(ChangePasswordViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PasswordChangeSucceeded += OnPasswordChangeSucceeded;
        _viewModel.Cancelled += OnCancelled;
    }

    private void OnPasswordChangeSucceeded(object? sender, EventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        DialogResult = false;
    }

    private void CurrentPasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.CurrentPassword = ((PasswordBox)sender).Password;
    }

    private void NewPasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.NewPassword = ((PasswordBox)sender).Password;
    }

    private void ConfirmNewPasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.ConfirmNewPassword = ((PasswordBox)sender).Password;
    }
}
