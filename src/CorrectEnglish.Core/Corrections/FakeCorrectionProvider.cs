using System.Diagnostics;

namespace CorrectEnglish.Core.Corrections;

/// <summary>
/// Motor falso da Fase 1. Deliberadamente idiota: devolve o texto em MAIUSCULAS.
/// <para>
/// O objetivo e validar o encanamento que mais quebra -- guardar a janela anterior,
/// devolver o foco, colar e restaurar o clipboard -- sem nenhuma IA na jogada.
/// Se este motor consegue trocar texto em Chrome, Slack, Word e VS Code,
/// a Fase 2 e so substituir esta classe.
/// </para>
/// </summary>
public sealed class FakeCorrectionProvider : ICorrectionProvider
{
    private readonly TimeSpan _simulatedLatency;

    /// <param name="simulatedLatency">
    /// Atraso artificial, para que o estado de carregamento da janela seja exercitado
    /// desde a Fase 1. A camada real vai levar 1 a 3 segundos.
    /// </param>
    public FakeCorrectionProvider(TimeSpan? simulatedLatency = null)
        => _simulatedLatency = simulatedLatency ?? TimeSpan.FromMilliseconds(450);

    public string Name => "Fake (fase 1)";

    public bool RequiresNetwork => false;

    public async Task<CorrectionResult> CorrectAsync(
        string text,
        Tone tone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var stopwatch = Stopwatch.StartNew();

        if (_simulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(_simulatedLatency, cancellationToken).ConfigureAwait(false);
        }

        var corrected = text.ToUpperInvariant();

        return new CorrectionResult
        {
            OriginalText = text,
            CorrectedText = corrected,
            TranslationPt = "A traducao chega na Fase 2, junto com a camada de IA.",
            Tone = tone,
            EngineName = Name,
            Elapsed = stopwatch.Elapsed,
            Confidence = 1.0,
            Corrections =
            [
                new Correction(
                    From: text.Length <= 24 ? text : text[..24] + "...",
                    To: corrected.Length <= 24 ? corrected : corrected[..24] + "...",
                    Kind: CorrectionKind.Other,
                    Why: "Motor de teste da Fase 1: tudo vira maiuscula. Serve para provar "
                         + "que o Replace devolve o texto no campo certo."),
            ],
            Alternatives = [text.ToLowerInvariant()],
        };
    }
}
