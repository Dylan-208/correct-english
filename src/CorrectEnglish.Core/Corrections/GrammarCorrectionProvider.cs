using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using CorrectEnglish.Core.Grammar;
using CorrectEnglish.Core.Translation;

namespace CorrectEnglish.Core.Corrections;

/// <summary>
/// Camada L1: gramática por regras, via LanguageToolL local.
/// <para>
/// Duas decisões importantes, ambas registradas na
/// <see href="../../../docs/adr/0006-gramatica-com-languagetool.md">ADR 0006</see>:
/// as regras de ortografia do LanguageTool são descartadas (a camada L0 já é dona de
/// ortografia, com proteções contra falso positivo que o LanguageTool não tem), e as
/// mensagens das regras são traduzidas para português com cache por texto.
/// </para>
/// </summary>
public sealed class GrammarCorrectionProvider : ICorrectionProvider
{
    /// <summary>
    /// Prefixo das regras do corretor ortográfico do LanguageTool.
    /// <para>
    /// É a <b>única</b> família descartada, e a fronteira é conceitual: a camada L0 é dona
    /// de "palavra que não existe no dicionário", e a L1 é dona de "palavra que existe mas
    /// está errada no contexto". MORFOLOGIK é exatamente a primeira coisa, e é também a
    /// única regra do LanguageTool que sublinharia <c>getUserById</c> ou <c>Dylan-208</c> --
    /// falso positivo que o tokenizador da L0 já sabe evitar.
    /// </para>
    /// </summary>
    private const string SpellCheckerRulePrefix = "MORFOLOGIK";

    private readonly ILanguageToolClient _client;
    private readonly ITranslator? _explanationTranslator;

    // Cache das mensagens traduzidas, por texto em inglês. O conjunto de regras é finito
    // e os erros de uma pessoa se repetem, então depois de alguns dias de uso quase toda
    // mensagem vem daqui de graça. É a estratégia prevista na ADR 0004.
    private readonly ConcurrentDictionary<string, string> _translatedMessages = new();

    /// <param name="explanationTranslator">
    /// Opcional. Quando ausente ou indisponível, a explicação fica em inglês -- degradação,
    /// não falha.
    /// </param>
    public GrammarCorrectionProvider(
        ILanguageToolClient client,
        ITranslator? explanationTranslator = null)
    {
        _client = client;
        _explanationTranslator = explanationTranslator;
    }

    public string Name => "LanguageTool";

    public bool RequiresNetwork => false; // localhost

    public async Task<CorrectionResult> CorrectAsync(
        string text,
        Tone tone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var stopwatch = Stopwatch.StartNew();

        var matches = await _client
            .CheckAsync(text, cancellationToken)
            .ConfigureAwait(false);

        var usable = matches
            .Where(IsWorthReporting)
            .OrderBy(m => m.Offset)
            .ToList();

        usable = RemoveOverlaps(usable);

        var corrections = new List<Correction>(usable.Count);
        var replacements = new List<(int Offset, int Length, string Value)>();

        foreach (var match in usable)
        {
            var original = text.Substring(match.Offset, match.Length);
            var best = match.Replacements.FirstOrDefault();

            corrections.Add(new Correction(
                From: original,
                To: best ?? original,
                Kind: MapKind(match),
                Why: await ExplainAsync(match, cancellationToken).ConfigureAwait(false)));

            if (best is not null)
            {
                replacements.Add((match.Offset, match.Length, best));
            }
        }

        return new CorrectionResult
        {
            OriginalText = text,
            CorrectedText = Apply(text, replacements),
            TranslationPt = string.Empty, // esta camada não traduz o texto
            Tone = tone,
            EngineName = Name,
            Elapsed = stopwatch.Elapsed,
            Confidence = corrections.Count == 0 ? 1.0 : 0.85,
            Corrections = corrections,
        };
    }

    /// <summary>
    /// Descarta o que a camada L0 já cobre, e o que não tem como ser aplicado.
    /// <para>
    /// <b>Não</b> filtra por <c>issueType</c> nem por categoria, e isso foi aprendido na
    /// prática: o LanguageTool marca <c>EN_A_VS_AN</c> (<c>a apple</c> → <c>an apple</c>)
    /// como <c>issueType: misspelling</c> e categoria <c>MISC</c>, apesar de ser erro de
    /// artigo. Filtrar por esses campos jogava fora exatamente a classe de erro que a
    /// camada L1 existe para pegar. Só o prefixo da regra é confiável.
    /// </para>
    /// </summary>
    private static bool IsWorthReporting(GrammarMatch match)
    {
        if (match.RuleId.StartsWith(SpellCheckerRulePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Sem sugestão de troca, a correção não é aplicável no Replace. Ainda assim vale
        // reportar, desde que haja explicação -- o usuário conserta à mão.
        return match.Replacements.Count > 0 || !string.IsNullOrWhiteSpace(match.Message);
    }

    /// <summary>
    /// Descarta problemas que se sobrepõem a um anterior.
    /// <para>
    /// O LanguageTool pode apontar duas regras no mesmo trecho. Aplicar as duas
    /// corromperia o texto: a segunda substituição usaria deslocamentos calculados sobre
    /// o texto de antes da primeira. Fica a de menor deslocamento.
    /// </para>
    /// </summary>
    private static List<GrammarMatch> RemoveOverlaps(List<GrammarMatch> ordered)
    {
        var kept = new List<GrammarMatch>(ordered.Count);
        var lastEnd = -1;

        foreach (var match in ordered)
        {
            if (match.Offset < lastEnd)
            {
                continue;
            }

            kept.Add(match);
            lastEnd = match.Offset + match.Length;
        }

        return kept;
    }

    private static string Apply(string text, List<(int Offset, int Length, string Value)> replacements)
    {
        if (replacements.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text);

        // De trás para frente, para os deslocamentos seguintes seguirem válidos.
        for (var i = replacements.Count - 1; i >= 0; i--)
        {
            var (offset, length, value) = replacements[i];

            if (offset < 0 || offset + length > builder.Length)
            {
                continue;
            }

            builder.Remove(offset, length);
            builder.Insert(offset, value);
        }

        return builder.ToString();
    }

    private static CorrectionKind MapKind(GrammarMatch match)
    {
        var rule = match.RuleId.ToUpperInvariant();

        if (rule.Contains("A_VS_AN", StringComparison.Ordinal)
            || rule.Contains("DT_", StringComparison.Ordinal)
            || rule.Contains("ARTICLE", StringComparison.Ordinal))
        {
            return CorrectionKind.Article;
        }

        if (rule.Contains("PREPOSITION", StringComparison.Ordinal))
        {
            return CorrectionKind.Preposition;
        }

        if (rule.Contains("TENSE", StringComparison.Ordinal)
            || rule.Contains("VERB", StringComparison.Ordinal)
            || rule.Contains("AGREEMENT", StringComparison.Ordinal))
        {
            return CorrectionKind.VerbTense;
        }

        return match.CategoryId.ToUpperInvariant() switch
        {
            "PUNCTUATION" or "TYPOGRAPHY" => CorrectionKind.Punctuation,
            "STYLE" or "REDUNDANCY" or "COLLOCATIONS" or "PLAIN_ENGLISH" => CorrectionKind.Naturalness,
            "GRAMMAR" => CorrectionKind.VerbTense,
            _ => CorrectionKind.Other,
        };
    }

    /// <summary>
    /// Traduz a mensagem da regra, com cache. Falha mantém o inglês.
    /// </summary>
    private async Task<string> ExplainAsync(GrammarMatch match, CancellationToken cancellationToken)
    {
        var message = match.Message;

        if (string.IsNullOrWhiteSpace(message))
        {
            return "O LanguageTool apontou um problema aqui, sem descrever qual.";
        }

        if (_explanationTranslator is null)
        {
            return message;
        }

        if (_translatedMessages.TryGetValue(message, out var cached))
        {
            return cached;
        }

        try
        {
            var translated = await _explanationTranslator
                .TranslateAsync(message, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(translated))
            {
                return message;
            }

            _translatedMessages[message] = translated;
            return translated;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return message;
        }
    }
}
