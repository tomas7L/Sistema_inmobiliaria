using System.Windows;
using System.Windows.Controls;

namespace Inmobiliaria.Desktop;

/// <summary>
/// The first window the bootstrap ever shows (design Decision 10). Its only job past
/// construction is to relay <see cref="LoginViewModel.LoginSucceeded"/> into
/// <see cref="Window.DialogResult"/>, so <c>App.xaml.cs</c> can read
/// <see cref="LoginViewModel.SuccessResult"/> once <c>ShowDialog()</c> returns.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        DialogResult = true;
    }

    /// <summary>
    /// <see cref="PasswordBox"/> deliberately does not support data binding (a WPF security
    /// choice, not an oversight this project can work around), so the password value is
    /// relayed to the ViewModel here instead.
    /// </summary>
    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Password = ((PasswordBox)sender).Password;
    }
}
