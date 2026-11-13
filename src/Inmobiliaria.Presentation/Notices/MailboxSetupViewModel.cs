using System.ComponentModel;
using System.Net.Mail;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Notices;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Notices;

/// <summary>
/// Entering or replacing the Gmail address and app password this PC sends from (notificaciones design §4.2, spec Decision
/// 18). Offered to an Admin only, by <see cref="NoticesViewModel"/>. It never shows a stored password back: it says only
/// whether one is stored, and for which address, and clears the password field once it is saved.
/// </summary>
public sealed partial class MailboxSetupViewModel : ObservableObject
{
    private readonly IMailboxCredentialStore _store;

    /// <param name="store">This PC's Windows Credential Manager entry (design D9).</param>
    /// <param name="errorLog">Where a failed save is kept, already without the password; the application's log when omitted.</param>
    public MailboxSetupViewModel(IMailboxCredentialStore store, IErrorLog? errorLog = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        Runner = errorLog is null ? new OperationRunner() : new OperationRunner(errorLog);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        Runner.PropertyChanged += OnRunnerChanged;

        MailboxCredentials? stored;
        try
        {
            stored = store.Read();
        }
        catch (Exception exception)
        {
            // Opening the setup never fails on an unreadable credential: it reads as not set up, and saving replaces it.
            (errorLog ?? OperationRunner.DefaultErrorLog).Record("Mailbox", exception);
            stored = null;
        }

        _address = stored?.Address ?? string.Empty;
        _storedText = stored is null ? NoticesTexts.MailboxNotStored : NoticesTexts.MailboxStored(stored.Address);
    }

    /// <summary>Raised after a save, so the screen can enable sending without reading the list again.</summary>
    public event EventHandler? Saved;

    public OperationRunner Runner { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _address;

    /// <summary>What the Admin types; empty again once saved. Never filled from the store.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _appPassword = string.Empty;

    /// <summary>Whether a mailbox is stored on this PC, and which.</summary>
    [ObservableProperty]
    private string _storedText;

    private bool CanSave() =>
        !Runner.IsBusy
        && !string.IsNullOrWhiteSpace(AppPassword)
        && MailAddress.TryCreate(Address.Trim(), out var parsed)
        && parsed.Address == Address.Trim();

    private async Task SaveAsync()
    {
        var credentials = new MailboxCredentials(Address.Trim(), AppPassword);
        var outcome = await Runner.RunAsync(
            () =>
            {
                try
                {
                    _store.Save(credentials);
                }
                catch (Exception exception)
                {
                    // The runner logs what it catches, and the store's message may quote what it was given: only a
                    // scrubbed copy, without the original as its inner exception, reaches the log (spec Requirement 6).
                    throw new InvalidOperationException(SecretMask.Mask(exception.Message, credentials.AppPassword));
                }

                return Task.FromResult(true);
            },
            OperationKind.Commit);

        if (!outcome.Completed)
        {
            // The cash desk's commit message speaks of a payment; this one says what failed.
            Runner.ErrorMessage = NoticesTexts.SaveFailed;
            return;
        }

        Address = credentials.Address;
        AppPassword = string.Empty;
        StoredText = NoticesTexts.MailboxStored(credentials.Address);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void OnRunnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationRunner.IsBusy))
        {
            SaveCommand.NotifyCanExecuteChanged();
        }
    }
}
