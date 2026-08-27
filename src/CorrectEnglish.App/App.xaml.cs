using System.Windows;
using System.Windows.Interop;
using CorrectEnglish.Core.Correction;
using CorrectEnglish.Core.Spelling;
using CorrectEnglish.Interop;
using CorrectEnglish.Interop.Clipboard;
using CorrectEnglish.Interop.Keyboard;
using CorrectEnglish.Interop.Windows;

// O projeto usa WPF e WinForms juntos (o NotifyIcon da bandeja e o Screen vem do WinForms),
// e os dois namespaces entram por ImplicitUsings. Estes aliases resolvem a ambiguidade
// sempre em favor do WPF, que e onde a UI vive.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace CorrectEnglish;

public partial class App : Application
{
    private readonly ClipboardService _clipboard = new();

    // Fase 2: camada L0 (Hunspell). Carregado no construtor de proposito -- gastar ~200 ms
    // na inicializacao de um app de bandeja e melhor do que atrasar o primeiro Ctrl+C x3.
    private readonly ICorrectionProvider _provider = CreateProvider();

    private TextReplacementService? _replacer;
    private TripleCopyWatcher? _watcher;
    private TrayIcon? _tray;
    private PopupWindow? _popup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                args.Exception.ToString(),
                "Correct English - erro nao tratado",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        _replacer = new TextReplacementService(_clipboard);

        _tray = new TrayIcon();
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.AboutRequested += (_, _) => ShowAbout();

        try
        {
            // Instalado a partir da thread de UI de proposito: um hook WH_KEYBOARD_LL
            // precisa de uma thread com bomba de mensagens, e o Dispatcher do WPF fornece.
            _watcher = new TripleCopyWatcher();
            _watcher.TripleCopyDetected += OnTripleCopyDetected;
            _watcher.Start();

            _tray.Notify(
                $"Correct English esta rodando ({_provider.Name})",
                "Selecione um texto e aperte Ctrl+C tres vezes.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Nao foi possivel instalar o hook de teclado.\n\n{ex.Message}",
                "Correct English",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Monta a camada de correcao. Quando o dicionario nao esta instalado, devolve um
    /// provedor que explica isso na propria janela, em vez de o app falhar em silencio
    /// ou de o codigo se encher de casos especiais.
    /// </summary>
    private static ICorrectionProvider CreateProvider()
    {
        var located = DictionaryLocator.TryLocate();

        if (located is null)
        {
            return new UnavailableCorrectionProvider(
                "O dicionario en_US nao foi encontrado. Rode "
                + "scripts\\get-dictionaries.ps1 para baixa-lo (2 arquivos, ~1 MB) "
                + "e reinicie o app.");
        }

        try
        {
            var checker = HunspellSpellChecker.FromFiles(
                located.Value.DictionaryPath,
                located.Value.AffixPath,
                name: "en_US");

            return new SpellingCorrectionProvider(checker);
        }
        catch (Exception ex)
        {
            return new UnavailableCorrectionProvider(
                $"O dicionario foi encontrado mas nao pode ser carregado: {ex.Message}");
        }
    }

    private void ShowAbout()
        => MessageBox.Show(
            "Correct English 0.1.0 - Fase 2\n\n"
            + $"Motor de correcao: {_provider.Name}\n"
            + (_provider.RequiresNetwork ? "Usa rede.\n" : "Funciona offline, sem custo.\n")
            + "\nEsta versao corrige ortografia. Gramatica (LanguageTool) e o aviso em\n"
            + "tempo real chegam nas fases seguintes.\n\n"
            + "github.com/Dylan-208/correct-english",
            "Sobre",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    /// <summary>
    /// Chamado de dentro do hook de teclado. Faz apenas o que e barato e sai:
    /// o Windows desinstala hooks que estouram <c>LowLevelHooksTimeout</c> (300 ms).
    /// </summary>
    private void OnTripleCopyDetected(object? sender, EventArgs e)
    {
        // A captura tem que vir antes de qualquer janela nossa aparecer.
        var target = ForegroundWindow.Capture();

        if (!target.IsValid)
        {
            return;
        }

        // O usuario apertou Ctrl+C dentro da nossa propria janela: ignora.
        if (_popup is not null && new WindowInteropHelper(_popup).Handle == target.Handle)
        {
            return;
        }

        var anchor = CaretLocator.GetAnchorPoint(target.Handle);

        Dispatcher.BeginInvoke(new Action(() => _ = HandleTriggerAsync(target, anchor)));
    }

    private async Task HandleTriggerAsync(WindowSnapshot target, ScreenPoint anchor)
    {
        try
        {
            _popup?.TryClose();

            var popup = new PopupWindow();
            _popup = popup;

            popup.Closed += (_, _) =>
            {
                if (ReferenceEquals(_popup, popup))
                {
                    _popup = null;
                }
            };

            popup.ReplaceRequested += async (_, text) =>
                await ReplaceAsync(popup, target, text).ConfigureAwait(true);

            popup.ShowAt(anchor);
            popup.ShowLoading();

            // O app de origem ainda pode estar processando o terceiro Ctrl+C.
            var selection = await _clipboard
                .WaitForTextAsync(TimeSpan.FromMilliseconds(500))
                .ConfigureAwait(true);

            if (!popup.IsVisible)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(selection))
            {
                popup.ShowMessage(
                    "Nao achei texto no clipboard. Selecione o texto antes de apertar "
                    + "Ctrl+C tres vezes.");
                return;
            }

            popup.ShowLoading($"Corrigindo com {_provider.Name}...");

            var result = await _provider
                .CorrectAsync(selection, Tone.Neutral)
                .ConfigureAwait(true);

            if (popup.IsVisible)
            {
                popup.ShowResult(result, target.Title);
            }
        }
        catch (Exception ex)
        {
            _popup?.ShowMessage($"Erro inesperado: {ex.Message}", isError: true);
        }
    }

    private async Task ReplaceAsync(PopupWindow popup, WindowSnapshot target, string text)
    {
        if (_replacer is null)
        {
            return;
        }

        // Impede a janela de se fechar sozinha quando o foco voltar para o alvo.
        popup.IsReplacing = true;

        try
        {
            var outcome = await _replacer.ReplaceAsync(target, text).ConfigureAwait(true);

            if (outcome.IsSuccess)
            {
                // IsReplacing continua true de proposito, e nao e reposta depois.
                // Fechar dispara Deactivated; se a guarda fosse desligada antes disso
                // (por exemplo num finally, que roda depois do Close), o manipulador
                // tentaria fechar uma janela ja em fechamento e lancaria excecao.
                popup.TryClose();
                return;
            }

            popup.ShowMessage(
                outcome.Detail ?? "Nao consegui trocar o texto.",
                isError: true);
        }
        catch (Exception ex)
        {
            popup.ShowMessage($"Erro no Replace: {ex.Message}", isError: true);
        }

        // So chega aqui quando a janela permanece aberta mostrando um erro. Reabilitar o
        // fechamento automatico deixa o usuario dispensar a janela clicando fora dela.
        popup.IsReplacing = false;
    }
}
