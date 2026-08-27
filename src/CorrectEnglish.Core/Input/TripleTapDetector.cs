namespace CorrectEnglish.Core.Input;

/// <summary>
/// Detecta N toques na mesma tecla dentro de uma janela de tempo -- o gatilho
/// <c>Ctrl+C Ctrl+C Ctrl+C</c> do app.
/// <para>
/// Logica pura de proposito: o timestamp e injetado pelo chamador, entao o
/// comportamento e testavel sem hook de teclado, sem Windows e sem esperar em tempo real.
/// </para>
/// </summary>
public sealed class TripleTapDetector
{
    private readonly Queue<TimeSpan> _taps = new();

    /// <param name="tapsRequired">Quantos toques completam a sequencia.</param>
    /// <param name="window">
    /// Tempo maximo entre o primeiro e o ultimo toque. 600 ms e confortavel: rapido
    /// o bastante para nao disparar em dois Ctrl+C acidentais, lento o bastante
    /// para nao exigir destreza.
    /// </param>
    public TripleTapDetector(int tapsRequired = 3, TimeSpan? window = null)
    {
        if (tapsRequired < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tapsRequired), tapsRequired, "A sequencia precisa de ao menos 2 toques.");
        }

        var resolvedWindow = window ?? TimeSpan.FromMilliseconds(600);
        if (resolvedWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(window), resolvedWindow, "A janela precisa ser positiva.");
        }

        TapsRequired = tapsRequired;
        Window = resolvedWindow;
    }

    public int TapsRequired { get; }

    public TimeSpan Window { get; }

    /// <summary>Quantos toques ja estao acumulados na janela atual.</summary>
    public int PendingTaps => _taps.Count;

    /// <summary>
    /// Registra um toque e diz se a sequencia se completou.
    /// </summary>
    /// <param name="timestamp">
    /// Instante do toque, de um relogio monotonico (ex.: <c>Stopwatch.Elapsed</c>).
    /// Nao use a hora do sistema: ela pode andar para tras.
    /// </param>
    /// <returns>
    /// <c>true</c> exatamente uma vez, no toque que completa a sequencia. O contador
    /// zera em seguida, entao a proxima sequencia comeca do zero.
    /// </returns>
    public bool RegisterTap(TimeSpan timestamp)
    {
        // Relogio andou para tras (troca de fonte de tempo, hibernacao): recomeca.
        if (_taps.Count > 0 && timestamp < _taps.Last())
        {
            Reset();
        }

        _taps.Enqueue(timestamp);

        // Descarta os toques que ficaram velhos demais para contar.
        while (_taps.Count > 0 && timestamp - _taps.Peek() > Window)
        {
            _taps.Dequeue();
        }

        if (_taps.Count < TapsRequired)
        {
            return false;
        }

        Reset();
        return true;
    }

    public void Reset() => _taps.Clear();
}
