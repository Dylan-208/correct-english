using System.Diagnostics;
using System.Runtime.InteropServices;
using CorrectEnglish.Interop.Native;
using WpfClipboard = System.Windows.Clipboard;

namespace CorrectEnglish.Interop.Clipboard;

/// <summary>
/// Acesso ao clipboard com nova tentativa em caso de falha.
/// <para>
/// O clipboard do Windows e um recurso global com dono exclusivo: qualquer app pode
/// estar segurando ele no instante em que tentamos abrir, e a chamada falha com
/// <c>CLIPBRD_E_CANT_OPEN</c>. Isso nao e caso excepcional, e rotina -- gerenciadores
/// de clipboard, o proprio Office, e o Teams fazem isso o tempo todo. Por isso toda
/// operacao aqui tenta de novo antes de desistir.
/// </para>
/// <para><b>Chame sempre de uma thread STA</b> -- a thread de UI do WPF e STA.</para>
/// </summary>
public sealed class ClipboardService
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    public ClipboardService(int maxAttempts = 8, TimeSpan? retryDelay = null)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(25);
    }

    /// <summary>
    /// Muda a cada alteracao do clipboard, por qualquer processo. Serve para saber se
    /// o conteudo trocou sem precisar ler (e sem precisar abrir o clipboard).
    /// </summary>
    public static uint SequenceNumber => NativeMethods.GetClipboardSequenceNumber();

    /// <summary>Le o texto do clipboard, ou <c>null</c> se nao houver texto ou se falhar.</summary>
    public string? TryGetText()
    {
        for (var attempt = 0; attempt < _maxAttempts; attempt++)
        {
            try
            {
                return WpfClipboard.ContainsText() ? WpfClipboard.GetText() : null;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                Sleep();
            }
        }

        return null;
    }

    /// <summary>
    /// Espera o clipboard ter texto, com limite de tempo.
    /// <para>
    /// Necessario porque o app de origem ainda esta processando o terceiro <c>Ctrl+C</c>
    /// quando o hook nos avisa. Ler imediatamente pega o conteudo antigo.
    /// </para>
    /// </summary>
    public async Task<string?> WaitForTextAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < timeout)
        {
            var text = TryGetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(true);
        }

        return TryGetText();
    }

    public bool TrySetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return false;
        }

        for (var attempt = 0; attempt < _maxAttempts; attempt++)
        {
            try
            {
                // copy: true entrega os dados ao clipboard, para o conteudo sobreviver
                // ao fim do nosso processo.
                WpfClipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                Sleep();
            }
        }

        return false;
    }

    private static bool IsTransient(Exception ex)
        => ex is COMException or ExternalException or UnauthorizedAccessException;

    private void Sleep() => Thread.Sleep(_retryDelay);
}
