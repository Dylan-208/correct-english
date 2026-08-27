using System.Diagnostics;
using CorrectEnglish.Core.Translation;

namespace CorrectEnglish.Core.Correction;

/// <summary>
/// Acrescenta tradução para português ao resultado de qualquer outro provedor.
/// <para>
/// É um decorador, não uma camada nova: não sabe nada sobre ortografia nem gramática.
/// Quando a camada L1 (LanguageTool) entrar, ela ganha tradução sem uma linha a mais.
/// </para>
/// <para>
/// Traduz o texto <b>corrigido</b>, não o original — erro de digitação degrada tradução
/// automática de forma desproporcional. Ver
/// <see href="../../../docs/adr/0005-traducao-com-libretranslate.md">ADR 0005</see>.
/// </para>
/// </summary>
public sealed class TranslatingCorrectionProvider : ICorrectionProvider
{
    private readonly ICorrectionProvider _inner;
    private readonly ITranslator _translator;

    public TranslatingCorrectionProvider(ICorrectionProvider inner, ITranslator translator)
    {
        _inner = inner;
        _translator = translator;
    }

    public string Name => $"{_inner.Name} + {_translator.Name}";

    public bool RequiresNetwork => _inner.RequiresNetwork;

    public async Task<CorrectionResult> CorrectAsync(
        string text,
        Tone tone,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var result = await _inner
            .CorrectAsync(text, tone, cancellationToken)
            .ConfigureAwait(false);

        string? translation;

        try
        {
            translation = await _translator
                .TranslateAsync(result.CorrectedText, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Corrigir é a função principal; traduzir é acréscimo. Um defeito no tradutor
            // não tem direito de derrubar a correção que já ficou pronta.
            translation = null;
        }

        if (translation is null)
        {
            return result with { Elapsed = stopwatch.Elapsed };
        }

        return result with
        {
            TranslationPt = translation,
            EngineName = $"{result.EngineName} + {_translator.Name}",
            Elapsed = stopwatch.Elapsed,
        };
    }
}
