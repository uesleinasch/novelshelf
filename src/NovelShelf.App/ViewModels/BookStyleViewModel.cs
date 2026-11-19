using System.Net;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelShelf.App.Services;
using NovelShelf.Core.Library;
using NovelShelf.Ebook;

namespace NovelShelf.App.ViewModels;

public sealed record PreviewParagraph(string Text, double IndentWidth, Thickness Margin, TextAlignment Alignment);

public sealed partial class BookStyleViewModel : ViewModelBase
{
    private const double PreviewFontSize = 17;

    private static readonly string[] FallbackSample =
    [
        "A chuva tinha parado pouco antes do amanhecer, e a cidade ainda cheirava a pedra molhada quando ele saiu da estalagem.",
        "— Você vai mesmo seguir sozinho? — perguntou a velha da recepção, sem tirar os olhos do livro de contas.",
        "Ele não respondeu. Ajeitou a bolsa no ombro, conferiu a espada mais uma vez e desceu a rua em direção ao portão leste.",
        "Ninguém sabia ao certo o que havia além da floresta, mas todos concordavam que voltar de lá era raro. Talvez fosse exatamente por isso que ele precisava ir.",
    ];

    private readonly AppServices _services;
    private readonly LibraryNovel? _novel;
    private readonly Action _close;
    private readonly IReadOnlyList<string> _sample;
    private readonly string _sampleTitle;
    private bool _loading;

    public BookStyleViewModel(AppServices services, LibraryNovel? novel, Action close)
    {
        _services = services;
        _novel = novel;
        _close = close;
        var chapter = novel is null ? null : services.Library.GetFirstChapter(novel.Id);
        var paragraphs = chapter is null ? [] : SampleParagraphs(chapter.Html);
        _sample = paragraphs.Count > 0 ? paragraphs : FallbackSample;
        _sampleTitle = chapter?.Title ?? "Capítulo 1 - O portão leste";

        var own = novel is null ? null : services.Library.GetBookStyle(novel.Id);
        _loading = true;
        UseDefault = novel is not null && own is null;
        _loading = false;
        Load(own ?? services.Settings.BookStyle);
    }

    public bool IsForNovel => _novel is not null;
    public string Title => _novel?.Title ?? "Padrão para todas as novels";
    public string Intro => IsForNovel
        ? "Ajuste como esta novel aparece no Kindle. Desligue \"Usar o estilo padrão\" para mudar só ela."
        : "Vale para todas as novels que não têm estilo próprio. Tamanho e tipo da letra continuam no menu Aa do Kindle.";
    public string SaveLabel => IsForNovel ? "Salvar para esta novel" : "Salvar padrão";
    public double PreviewSize => PreviewFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _useDefault;

    public bool CanEdit => !UseDefault;

    [ObservableProperty]
    private double _indent;

    [ObservableProperty]
    private double _paragraphSpacing;

    [ObservableProperty]
    private bool _justify;

    [ObservableProperty]
    private int _headingIndex;

    [ObservableProperty]
    private IReadOnlyList<PreviewParagraph> _paragraphs = [];

    [ObservableProperty]
    private string? _previewHeading;

    [ObservableProperty]
    private string? _message;

    private BookStyle Style => new BookStyle
    {
        Indent = Indent,
        ParagraphSpacing = ParagraphSpacing,
        Justify = Justify,
        Heading = (ChapterHeading)HeadingIndex,
    }.Normalized();

    private void Load(BookStyle style)
    {
        _loading = true;
        Indent = style.Indent;
        ParagraphSpacing = style.ParagraphSpacing;
        Justify = style.Justify;
        HeadingIndex = (int)style.Heading;
        _loading = false;
        RefreshPreview();
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading)
            return;
        switch (e.PropertyName)
        {
            case nameof(UseDefault):
                Message = null;
                if (UseDefault)
                    Load(_services.Settings.BookStyle);
                break;
            case nameof(Indent):
            case nameof(ParagraphSpacing):
            case nameof(Justify):
            case nameof(HeadingIndex):
                Message = null;
                RefreshPreview();
                break;
        }
    }

    private void RefreshPreview()
    {
        var style = Style;
        var margin = new Thickness(0, 0, 0, style.ParagraphSpacing * PreviewFontSize);
        var alignment = style.Justify ? TextAlignment.Justify : TextAlignment.Left;
        Paragraphs = _sample
            .Select(text => new PreviewParagraph(text, style.Indent * PreviewFontSize, margin, alignment))
            .ToList();
        PreviewHeading = style.ShowsHeading ? style.ChapterTitle(1, _sampleTitle) : null;
    }

    [RelayCommand]
    private void Save()
    {
        if (_novel is not null)
        {
            _services.Library.SetBookStyle(_novel.Id, UseDefault ? null : Style);
        }
        else if (Style != _services.Settings.BookStyle)
        {
            _services.Settings.BookStyle = Style;
            _services.Settings.Save();
            _services.Library.MarkDefaultStyleChanged();
        }
        Message = "Estilo salvo. Os livros já enviados ficam marcados para reenviar: envie de novo para o Kindle receber o novo visual.";
    }

    [RelayCommand]
    private void Reset()
    {
        UseDefault = false;
        Load(BookStyle.Default);
    }

    [RelayCommand]
    private void Close() => _close();

    private static List<string> SampleParagraphs(string html) =>
        Regex.Matches(html, @"<p\b[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .Select(m => WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim())
            .Where(t => t.Length > 0)
            .Take(6)
            .ToList();
}
