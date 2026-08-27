using CorrectEnglish.Interop.Clipboard;
using CorrectEnglish.Interop.Input;
using CorrectEnglish.Interop.Windows;

namespace CorrectEnglish.Interop;

public enum ReplacementStatus
{
    Success,

    /// <summary>A janela alvo deixou de existir enquanto a janela do app estava aberta.</summary>
    TargetWindowGone,

    /// <summary>O Windows recusou devolver o foco para a janela alvo.</summary>
    FocusRestoreFailed,

    /// <summary>Outro processo segurou o clipboard alem do limite de tentativas.</summary>
    ClipboardUnavailable,

    /// <summary>O <c>SendInput</c> foi recusado -- tipicamente app elevado (UIPI).</summary>
    KeystrokeRejected,
}

public sealed record ReplacementOutcome(ReplacementStatus Status, string? Detail = null)
{
    public bool IsSuccess => Status == ReplacementStatus.Success;
}

/// <summary>
/// O <c>Replace</c>: devolve o texto corrigido para o campo de onde ele saiu.
/// <para>
/// Esta e a classe que a Fase 1 existe para validar. Nada de IA envolvido -- se a
/// sequencia abaixo funcionar em Chrome, Slack, Word e VS Code, todo o resto do projeto
/// e substituir o motor de correcao. Se nao funcionar, nenhum modelo salva o app.
/// </para>
/// </summary>
public sealed class TextReplacementService
{
    private readonly ClipboardService _clipboard;
    private readonly TimeSpan _focusSettleDelay;
    private readonly TimeSpan _pasteSettleDelay;

    /// <param name="focusSettleDelay">
    /// Espera depois de devolver o foco. Colar antes do app terminar de reativar
    /// manda o Ctrl+V para o vazio.
    /// </param>
    /// <param name="pasteSettleDelay">
    /// Espera antes de restaurar o clipboard. Muitos apps leem o clipboard de forma
    /// assincrona depois do Ctrl+V; restaurar cedo demais faz o usuario colar o texto
    /// antigo. Este valor e o mais fragil do arquivo -- se algum app colar o conteudo
    /// errado, e aqui que se aumenta.
    /// </param>
    public TextReplacementService(
        ClipboardService clipboard,
        TimeSpan? focusSettleDelay = null,
        TimeSpan? pasteSettleDelay = null)
    {
        _clipboard = clipboard;
        _focusSettleDelay = focusSettleDelay ?? TimeSpan.FromMilliseconds(80);
        _pasteSettleDelay = pasteSettleDelay ?? TimeSpan.FromMilliseconds(200);
    }

    public async Task<ReplacementOutcome> ReplaceAsync(
        WindowSnapshot target,
        string replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(replacement);

        if (!target.StillExists)
        {
            return new ReplacementOutcome(
                ReplacementStatus.TargetWindowGone,
                $"A janela \"{target.Title}\" nao existe mais.");
        }

        // Guarda o que estava no clipboard para devolver depois. Como o gatilho foi
        // Ctrl+C, isto e a propria selecao do usuario -- restaurar deixa o clipboard
        // exatamente como ele esperava encontrar.
        var previousClipboardText = _clipboard.TryGetText();

        if (!ForegroundWindow.TryRestore(target.Handle))
        {
            return new ReplacementOutcome(
                ReplacementStatus.FocusRestoreFailed,
                $"O Windows nao devolveu o foco para \"{target.Title}\".");
        }

        await Task.Delay(_focusSettleDelay, cancellationToken).ConfigureAwait(true);

        if (!_clipboard.TrySetText(replacement))
        {
            return new ReplacementOutcome(
                ReplacementStatus.ClipboardUnavailable,
                "Outro programa esta segurando o clipboard.");
        }

        if (!KeystrokeSender.SendPaste())
        {
            // Devolve o clipboard antes de sair, para nao deixar a correcao la sozinha.
            await RestoreClipboardAsync(previousClipboardText, cancellationToken)
                .ConfigureAwait(true);

            return new ReplacementOutcome(
                ReplacementStatus.KeystrokeRejected,
                "O Windows recusou o envio de teclas. A janela alvo roda como administrador?");
        }

        await RestoreClipboardAsync(previousClipboardText, cancellationToken).ConfigureAwait(true);

        return new ReplacementOutcome(ReplacementStatus.Success);
    }

    private async Task RestoreClipboardAsync(string? previousText, CancellationToken cancellationToken)
    {
        await Task.Delay(_pasteSettleDelay, cancellationToken).ConfigureAwait(true);

        if (!string.IsNullOrEmpty(previousText))
        {
            _clipboard.TrySetText(previousText);
        }
    }
}
