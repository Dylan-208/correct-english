using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CorrectEnglish.Core.Corrections;
using CorrectEnglish.Interop.Windows;
using WinFormsScreen = System.Windows.Forms.Screen;

// Mesma razao do App.xaml.cs: WPF e WinForms coexistem no projeto, entao os tipos
// com nome repetido precisam ser desambiguados em favor do WPF.
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Clipboard = System.Windows.Clipboard;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace CorrectEnglish;

public partial class PopupWindow : Window
{
    private ScreenPoint _anchor;
    private string _correctedText = string.Empty;
    private bool _isClosing;

    public PopupWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += OnDeactivated;
    }

    /// <summary>Usuario pediu para aplicar a correcao. O argumento e o texto final.</summary>
    public event EventHandler<string>? ReplaceRequested;

    /// <summary>
    /// True enquanto o Replace esta em andamento.
    /// <para>
    /// Existe por um motivo especifico: o Replace devolve o foco para a janela alvo, o que
    /// dispara <c>Deactivated</c> nesta janela. Sem esta guarda, a janela se fecharia no meio
    /// da operacao e o <c>Ctrl+V</c> chegaria depois do clipboard ja ter sido restaurado.
    /// </para>
    /// </summary>
    public bool IsReplacing { get; set; }

    public void ShowAt(ScreenPoint anchor)
    {
        _anchor = anchor;
        Show();
        Activate();
    }

    /// <summary>
    /// Fecha a janela no maximo uma vez.
    /// <para>
    /// O <c>Close()</c> do WPF nao e idempotente: chamar de novo enquanto a janela esta
    /// fechando lanca <see cref="InvalidOperationException"/>. E fechar <i>provoca</i> a
    /// desativacao, que era justamente o nosso gatilho para fechar -- ou seja, o caminho
    /// natural do codigo era reentrante.
    /// </para>
    /// </summary>
    public void TryClose()
    {
        if (_isClosing)
        {
            return;
        }

        Close();
    }

    public void ShowLoading(string message = "Lendo o texto que voce selecionou...")
    {
        LoadingText.Text = message;
        SetState(loading: true, result: false, message: false);
    }

    public void ShowMessage(string text, bool isError = false)
    {
        MessageLabel.Text = isError ? "NAO DEU" : "AVISO";
        MessageLabel.Foreground = isError
            ? (Brush)FindResource("Accent")
            : (Brush)FindResource("Muted");
        MessageText.Text = text;
        SetState(loading: false, result: false, message: true);
    }

    public void ShowResult(CorrectionResult result, string targetTitle)
    {
        ArgumentNullException.ThrowIfNull(result);

        _correctedText = result.CorrectedText;

        var wordCount = result.OriginalText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        OriginalLabel.Text = $"SELECIONADO  ·  {wordCount} PALAVRAS";
        OriginalText.Text = Shorten(result.OriginalText, 400);
        CorrectedText.Text = result.CorrectedText;

        // A camada de ortografia nao traduz. Em vez de mostrar um campo vazio -- que
        // parece defeito -- a secao inteira desaparece quando nao ha traducao.
        var hasTranslation = !string.IsNullOrWhiteSpace(result.TranslationPt);
        TranslationSection.Visibility = hasTranslation ? Visibility.Visible : Visibility.Collapsed;

        if (hasTranslation)
        {
            TranslationText.Text = result.TranslationPt;
        }

        WhyList.ItemsSource = result.Corrections
            .Select(c => new WhyItem(Describe(c.Kind), c.Why))
            .ToList();
        WhySection.Visibility = result.Corrections.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        EngineLabel.Text = $"{result.EngineName}  ·  {result.Elapsed.TotalSeconds:0.0} s";
        TargetLabel.Text = Shorten(targetTitle, 60);
        ReplaceButton.IsEnabled = !result.IsUnchanged;
        ReplaceButton.ToolTip = result.IsUnchanged
            ? "O texto ja esta correto -- nada para trocar."
            : "Troca o texto selecionado pela correcao (Enter)";

        SetState(loading: false, result: true, message: false);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SizeChanged += (_, _) => PositionNearAnchor();
        PositionNearAnchor();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;

        // Desinscreve antes de fechar. O fechamento em si dispara Deactivated, e sem isto
        // o manipulador tentaria fechar uma janela que ja esta fechando.
        Deactivated -= OnDeactivated;
        PreviewKeyDown -= OnPreviewKeyDown;

        base.OnClosing(e);
    }

    private static string Shorten(string text, int max)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    private static string Describe(CorrectionKind kind) => kind switch
    {
        CorrectionKind.Spelling => "ortografia",
        CorrectionKind.VerbTense => "tempo verbal",
        CorrectionKind.Preposition => "preposicao",
        CorrectionKind.Article => "artigo",
        CorrectionKind.Naturalness => "naturalidade",
        CorrectionKind.Punctuation => "pontuacao",
        _ => "ajuste",
    };

    /// <summary>
    /// Converte o ponto ancora de pixels fisicos para DIPs e encaixa a janela na area de
    /// trabalho do monitor certo. Sem a conversao, a janela erra o lugar em telas com escala
    /// diferente de 100%.
    /// </summary>
    private void PositionNearAnchor()
    {
        var source = PresentationSource.FromVisual(this);
        var toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var anchorDip = toDip.Transform(new Point(_anchor.X, _anchor.Y));

        var workArea = WinFormsScreen
            .FromPoint(new System.Drawing.Point(_anchor.X, _anchor.Y))
            .WorkingArea;

        var areaTopLeft = toDip.Transform(new Point(workArea.Left, workArea.Top));
        var areaBottomRight = toDip.Transform(new Point(workArea.Right, workArea.Bottom));

        // Um pouco abaixo e a esquerda do caret, para nao cobrir o que foi escrito.
        var left = anchorDip.X - 20;
        var top = anchorDip.Y + 10;

        if (left + ActualWidth > areaBottomRight.X)
        {
            left = areaBottomRight.X - ActualWidth;
        }

        if (left < areaTopLeft.X)
        {
            left = areaTopLeft.X;
        }

        // Nao cabe embaixo: abre acima do caret.
        if (top + ActualHeight > areaBottomRight.Y)
        {
            top = anchorDip.Y - ActualHeight - 10;
        }

        if (top < areaTopLeft.Y)
        {
            top = areaTopLeft.Y;
        }

        Left = left;
        Top = top;
    }

    private void SetState(bool loading, bool result, bool message)
    {
        LoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = result ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = message ? Visibility.Visible : Visibility.Collapsed;
        ReplaceButton.IsEnabled = result;
        CopyButton.IsEnabled = result;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            TryClose();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Fecha ao perder o foco, como um flyout -- exceto durante o Replace,
        // que perde o foco de proposito.
        if (!IsReplacing)
        {
            TryClose();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => TryClose();

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_correctedText))
        {
            ReplaceRequested?.Invoke(this, _correctedText);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_correctedText))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetDataObject(_correctedText, copy: true);
            CopyButton.Content = "Copiado";
        }
        catch (Exception)
        {
            CopyButton.Content = "Falhou";
        }
    }

    private sealed record WhyItem(string Label, string Why);
}
