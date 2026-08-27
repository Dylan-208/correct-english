using WeCantSpell.Hunspell;

namespace CorrectEnglish.Core.Spelling;

/// <summary>
/// Ortografia via Hunspell -- o mesmo motor do LibreOffice, Firefox e Chrome, aqui como
/// port gerenciado (sem binário nativo para embarcar).
/// </summary>
public sealed class HunspellSpellChecker : ISpellChecker
{
    private readonly WordList _wordList;
    private readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);

    private HunspellSpellChecker(WordList wordList, string name)
    {
        _wordList = wordList;
        Name = name;
    }

    public string Name { get; }

    /// <summary>Carrega os arquivos <c>.dic</c> e <c>.aff</c> do dicionário.</summary>
    /// <exception cref="FileNotFoundException">Se algum dos dois arquivos não existir.</exception>
    public static HunspellSpellChecker FromFiles(
        string dictionaryPath,
        string affixPath,
        string? name = null)
    {
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException("Dicionário não encontrado.", dictionaryPath);
        }

        if (!File.Exists(affixPath))
        {
            throw new FileNotFoundException("Arquivo de afixos não encontrado.", affixPath);
        }

        var wordList = WordList.CreateFromFiles(dictionaryPath, affixPath);
        return new HunspellSpellChecker(
            wordList,
            name ?? Path.GetFileNameWithoutExtension(dictionaryPath));
    }

    /// <summary>
    /// Constrói um dicionário na memória a partir de uma lista de palavras.
    /// Existe para os testes rodarem sem depender de arquivo baixado.
    /// </summary>
    public static HunspellSpellChecker FromWords(IEnumerable<string> words, string name = "memória")
        => new(WordList.CreateFromWords(words), name);

    /// <summary>
    /// Troca apóstrofo tipográfico por apóstrofo reto.
    /// <para>
    /// Sem isto, <c>don’t</c> escrito no Word ou no WhatsApp -- que substituem o apóstrofo
    /// automaticamente -- seria acusado como erro, porque o dicionário en_US guarda
    /// <c>don't</c> com o caractere reto. Seria um sublinhado falso nas palavras mais
    /// comuns do inglês.
    /// </para>
    /// </summary>
    private static string Normalize(string word) => word.Replace('’', '\'');

    public bool IsCorrect(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return true;
        }

        if (_ignored.Contains(word))
        {
            return true;
        }

        word = Normalize(word);

        if (_wordList.Check(word))
        {
            return true;
        }

        // "Sentence" no início de frase e "SENTENCE" já foram filtrados pelo tokenizador,
        // mas uma palavra capitalizada no meio da frase (nome próprio, ou início de linha)
        // pode não estar no dicionário na forma capitalizada. Verifica em minúsculas antes
        // de acusar erro.
        var lowered = word.ToLowerInvariant();
        return lowered != word && _wordList.Check(lowered);
    }

    public IReadOnlyList<string> Suggest(string word, int max = 3)
    {
        if (string.IsNullOrWhiteSpace(word) || max <= 0)
        {
            return Array.Empty<string>();
        }

        var suggestions = _wordList.Suggest(Normalize(word)).Take(max).ToList();

        // O Hunspell devolve sugestões na forma do dicionário. Se a palavra original
        // começava com maiúscula, preserva -- senão o Replace estragaria o início da frase.
        if (word.Length > 0 && char.IsUpper(word[0]))
        {
            for (var i = 0; i < suggestions.Count; i++)
            {
                suggestions[i] = Capitalize(suggestions[i]);
            }
        }

        return suggestions;
    }

    public void Ignore(string word)
    {
        if (!string.IsNullOrWhiteSpace(word))
        {
            _ignored.Add(word);
        }
    }

    private static string Capitalize(string value)
        => value.Length == 0 || char.IsUpper(value[0])
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];
}
