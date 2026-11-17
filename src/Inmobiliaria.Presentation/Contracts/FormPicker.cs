using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inmobiliaria.Domain.Shared;

namespace Inmobiliaria.Presentation.Contracts;

/// <summary>One suggestion of a picker: what it is, how it reads, a short note beside it, and the text it is found by.</summary>
public sealed record PickerOption<T>(T Item, string Text, string? Tag, string SearchText);

/// <summary>
/// A field the operator types part of a name or an address into, and picks from what matches, as on the designer's canvas
/// "Nuevo contrato" (task 12.3d). It filters what was read once, in memory, so typing asks the database nothing; text is
/// compared as every search does (<see cref="TextFold"/>).
/// </summary>
public sealed partial class FormPicker<T> : ObservableObject
{
    /// <summary>Fewer meaningful characters than this suggest nothing, as in the account search.</summary>
    public const int MinimumLength = 2;

    /// <summary>The most suggestions shown at once.</summary>
    public const int MaxSuggestions = 8;

    private readonly Func<T, PickerOption<T>> _describe;
    private readonly Func<T, Task> _pick;
    private IReadOnlyList<PickerOption<T>> _options = [];

    public FormPicker(Func<T, PickerOption<T>> describe, Func<T, Task> pick)
    {
        ArgumentNullException.ThrowIfNull(describe);
        ArgumentNullException.ThrowIfNull(pick);

        _describe = describe;
        _pick = pick;
        PickCommand = new AsyncRelayCommand<PickerOption<T>>(PickAsync);
        PickFirstCommand = new AsyncRelayCommand(() => PickAsync(Suggestions.FirstOrDefault()));
    }

    public ObservableCollection<PickerOption<T>> Suggestions { get; } = [];

    /// <summary>Takes the suggestion, and empties the field for the next.</summary>
    public IAsyncRelayCommand<PickerOption<T>> PickCommand { get; }

    /// <summary>Enter: takes the first suggestion, if any (Tomás, 2026-11-17).</summary>
    public IAsyncRelayCommand PickFirstCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoMatch))]
    private string _query = string.Empty;

    /// <summary>Something long enough was typed and nothing matches it.</summary>
    public bool HasNoMatch => IsSearchable(Query) && Suggestions.Count == 0;

    /// <summary>What can be picked, read once when the form loads.</summary>
    public void SetOptions(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _options = items.Select(_describe).ToList();
        Filter();
    }

    partial void OnQueryChanged(string value) => Filter();

    private static bool IsSearchable(string text) => TextFold.Fold(text).Length >= MinimumLength;

    private void Filter()
    {
        Suggestions.Clear();
        if (IsSearchable(Query))
        {
            var folded = TextFold.Fold(Query);
            foreach (var option in _options.Where(o => o.SearchText.Contains(folded, StringComparison.Ordinal)).Take(MaxSuggestions))
            {
                Suggestions.Add(option);
            }
        }

        OnPropertyChanged(nameof(HasNoMatch));
    }

    private async Task PickAsync(PickerOption<T>? option)
    {
        if (option is null)
        {
            return;
        }

        Query = string.Empty;
        await _pick(option.Item);
    }
}
