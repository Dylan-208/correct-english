using System.Diagnostics;
using System.Text;
using CorrectEnglish.Core.Spelling;

namespace CorrectEnglish.Core.Correction;

/// <summary>
/// Camada L0 como provedor de correção: encontra palavras fora do dicionário e aplica a
/// sugestão mais provável de cada uma.
/// <para>
/// Deliberadamente honesto sobre o que não faz: não traduz, não avalia gramática e não
/// julga naturalidade. Se a palavra existe em inglês, esta camada está satisfeita — mesmo
/// que a frase esteja errada.
/// </para>
/// </summary>
public sealed class SpellingCorrectionProvider : ICorrectionProvider
{
    private readonly ISpellChecker _checker;

    public SpellingCorrectionProvider(ISpellChecker checker) => _checker = checker;

    public string Name => $"Hunspell {_checker.Name}";

    public bool RequiresNetwork => false;

    public Task<CorrectionResult> CorrectAsync(
        string text,
        Tone tone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();

        var tokens = EnglishTokenizer.Tokenize(text);
        var corrections = new List<Correction>();
        var replacements = new List<(WordToken Token, string Replacement)>();

        foreach (var token in tokens)
        {
            if (_checker.IsCorrect(token.Text))
            {
                continue;
            }

            var suggestions = _checker.Suggest(token.Text, max: 3);
            var best = suggestions.FirstOrDefault();

            corrections.Add(new Correction(
                From: token.Text,
                To: best ?? token.Text,
                Kind: CorrectionKind.Spelling,
                Why: BuildExplanation(token.Text, suggestions)));

            if (best is not null)
            {
                replacements.Add((token, best));
            }
        }

        var corrected = ApplyReplacements(text, replacements);

        return Task.FromResult(new CorrectionResult
        {
            OriginalText = text,
            CorrectedText = corrected,
            TranslationPt = string.Empty, // esta camada não traduz
            Tone = tone,
            EngineName = Name,
            Elapsed = stopwatch.Elapsed,
            Confidence = corrections.Count == 0 ? 1.0 : 0.8,
            Corrections = corrections,
            Alternatives = Array.Empty<string>(),
        });
    }

    /// <summary>
    /// Aplica as trocas de trás para frente, para que os deslocamentos das trocas
    /// seguintes continuem válidos.
    /// </summary>
    private static string ApplyReplacements(
        string text,
        List<(WordToken Token, string Replacement)> replacements)
    {
        if (replacements.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text);

        for (var i = replacements.Count - 1; i >= 0; i--)
        {
            var (token, replacement) = replacements[i];
            builder.Remove(token.Start, token.Length);
            builder.Insert(token.Start, replacement);
        }

        return builder.ToString();
    }

    private static string BuildExplanation(string word, IReadOnlyList<string> suggestions)
    {
        if (suggestions.Count == 0)
        {
            return $"Não encontrei \"{word}\" no dicionário de inglês, e não tenho uma "
                   + "sugestão próxima. Pode ser nome próprio, sigla ou gíria.";
        }

        var others = suggestions.Skip(1).ToList();
        var extra = others.Count > 0
            ? $" Outras possibilidades: {string.Join(", ", others)}."
            : string.Empty;

        return $"\"{word}\" não existe no dicionário de inglês. A grafia mais próxima é "
               + $"\"{suggestions[0]}\".{extra}";
    }
}
