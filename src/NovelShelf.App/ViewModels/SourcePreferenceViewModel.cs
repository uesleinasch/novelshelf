using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using NovelShelf.Suwayomi;

namespace NovelShelf.App.ViewModels;

public sealed partial class SourceOptionViewModel(SourceChoice choice, bool isChecked) : ViewModelBase
{
    public string Label => choice.Label;
    public string Value => choice.Value;

    [ObservableProperty]
    private bool _isChecked = isChecked;
}

public sealed partial class SourcePreferenceViewModel : ViewModelBase
{
    public SourcePreferenceViewModel(SourcePreference preference)
    {
        Original = preference;
        _text = preference.Text ?? "";
        _isOn = preference.IsOn;
        _choice = preference.Choices.FirstOrDefault(c => preference.Selected.Contains(c.Value));
        Options = preference.Choices.Select(c => new SourceOptionViewModel(c, preference.Selected.Contains(c.Value))).ToList();
    }

    public SourcePreference Original { get; }
    public string Title => Original.Title;
    public string? Summary => Original.Summary;
    public bool HasSummary => Summary is not null;
    public bool IsEnabled => Original.Enabled;
    public bool IsText => Original.Kind == SourcePreferenceKind.Text;
    public bool IsToggle => Original.Kind is SourcePreferenceKind.Switch or SourcePreferenceKind.CheckBox;
    public bool IsList => Original.Kind == SourcePreferenceKind.List;
    public bool IsMultiList => Original.Kind == SourcePreferenceKind.MultiList;
    public bool IsSecret => IsText && SecretName().IsMatch($"{Original.Key} {Original.Title}");
    public char PasswordChar => IsSecret ? '•' : '\0';
    public IReadOnlyList<SourceChoice> Choices => Original.Choices;
    public IReadOnlyList<SourceOptionViewModel> Options { get; }

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _isOn;

    [ObservableProperty]
    private SourceChoice? _choice;

    public SourcePreference? Edited()
    {
        var edited = Original.Kind switch
        {
            SourcePreferenceKind.Text => Original with { Text = Text },
            SourcePreferenceKind.Switch or SourcePreferenceKind.CheckBox => Original with { IsOn = IsOn },
            SourcePreferenceKind.List => Original with { Selected = Choice is { } choice ? [choice.Value] : Original.Selected },
            _ => Original with { Selected = Options.Where(o => o.IsChecked).Select(o => o.Value).ToList() },
        };
        var changed = Original.Kind switch
        {
            SourcePreferenceKind.Text => (Original.Text ?? "") != Text,
            SourcePreferenceKind.Switch or SourcePreferenceKind.CheckBox => Original.IsOn != IsOn,
            _ => !edited.Selected.Order().SequenceEqual(Original.Selected.Order()),
        };
        return changed ? edited : null;
    }

    [GeneratedRegex("senha|pass|token|secret|segredo", RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();
}
