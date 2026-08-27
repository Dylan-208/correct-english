using System.Diagnostics;

namespace CorrectEnglish.Core.Corrections;

/// <summary>
/// Encadeia camadas de correção: a saída de uma é a entrada da seguinte.
/// <para>
/// Sequencial, e não paralelo com fusão de resultados, por um motivo concreto: duas camadas
/// analisando o <i>mesmo</i> texto produzem deslocamentos calculados sobre a mesma base, e
/// aplicar os dois conjuntos corromperia o texto -- a segunda substituição usaria posições
/// de antes da primeira. Encadeando, cada camada recebe um texto já consistente e o
/// problema desaparece em vez de precisar ser resolvido.
/// </para>
/// <para>
/// O preço: a camada L1 nunca vê o erro de ortografia que a L0 consertou. É desejável --
/// gramática avaliada sobre texto com typo produz ruído.
/// </para>
/// </summary>
public sealed class PipelineCorrectionProvider : ICorrectionProvider
{
    private readonly IReadOnlyList<ICorrectionProvider> _stages;

    public PipelineCorrectionProvider(params ICorrectionProvider[] stages)
    {
        ArgumentNullException.ThrowIfNull(stages);

        if (stages.Length == 0)
        {
            throw new ArgumentException("O pipeline precisa de ao menos uma camada.", nameof(stages));
        }

        _stages = stages;
    }

    public string Name => string.Join(" + ", _stages.Select(stage => stage.Name));

    public bool RequiresNetwork => _stages.Any(stage => stage.RequiresNetwork);

    public async Task<CorrectionResult> CorrectAsync(
        string text,
        Tone tone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var stopwatch = Stopwatch.StartNew();

        var current = text;
        var corrections = new List<Correction>();
        var translation = string.Empty;
        var lowestConfidence = 1.0;

        foreach (var stage in _stages)
        {
            var result = await stage
                .CorrectAsync(current, tone, cancellationToken)
                .ConfigureAwait(false);

            current = result.CorrectedText;
            corrections.AddRange(result.Corrections);

            // Uma camada mais adiante pode acrescentar tradução; a última a fornecer vence.
            if (!string.IsNullOrWhiteSpace(result.TranslationPt))
            {
                translation = result.TranslationPt;
            }

            lowestConfidence = Math.Min(lowestConfidence, result.Confidence);
        }

        return new CorrectionResult
        {
            OriginalText = text,
            CorrectedText = current,
            TranslationPt = translation,
            Tone = tone,
            EngineName = Name,
            Elapsed = stopwatch.Elapsed,
            Confidence = lowestConfidence,
            Corrections = corrections,
        };
    }
}
