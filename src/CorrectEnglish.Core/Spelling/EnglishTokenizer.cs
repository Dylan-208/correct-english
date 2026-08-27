using System.Text.RegularExpressions;

namespace CorrectEnglish.Core.Spelling;

/// <summary>Uma palavra e sua posicao no texto original.</summary>
public readonly record struct WordToken(string Text, int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// Quebra texto em inglês em palavras verificáveis.
/// <para>
/// A qualidade do corretor depende mais desta classe do que do dicionário. Um dicionário
/// perfeito que recebe <c>getUserById</c> ou <c>dylansilva208@gmail.com</c> como palavras vai
/// sublinhar tudo e o usuário desliga o app no primeiro dia. Descartar o que não é palavra é
/// metade do trabalho.
/// </para>
/// </summary>
public static partial class EnglishTokenizer
{
    /// <summary>
    /// Trechos que não devem ser verificados de jeito nenhum: URLs, e-mails, menções,
    /// hashtags e domínios. Detectados antes da quebra em palavras, porque só fazem
    /// sentido como um todo.
    /// </summary>
    [GeneratedRegex(
        @"https?://\S+|www\.\S+|\S+@\S+\.\w+|[@#]\w+|\b\w+\.(?:com|net|org|io|dev|gov|edu|br|co)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SkipSpansRegex();

    /// <param name="text">Texto a analisar.</param>
    /// <param name="skipAllCaps">
    /// Descarta palavras totalmente em maiúsculas. Ligado por padrão: em texto técnico,
    /// MAIÚSCULAS é quase sempre sigla (API, HTTP, SQL, CNPJ), e nenhum dicionário tem
    /// todas. O custo é não pegar um erro digitado gritando.
    /// </param>
    public static IReadOnlyList<WordToken> Tokenize(string text, bool skipAllCaps = true)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return Array.Empty<WordToken>();
        }

        var skipRanges = SkipSpansRegex()
            .Matches(text)
            .Select(m => (Start: m.Index, End: m.Index + m.Length))
            .ToList();

        var tokens = new List<WordToken>();
        var index = 0;

        while (index < text.Length)
        {
            if (!IsWordStart(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            index = ScanWord(text, index);

            var length = index - start;
            var word = text.Substring(start, length);

            if (IsVerifiable(word, start, length, skipRanges, skipAllCaps))
            {
                tokens.Add(new WordToken(word, start, length));
            }
        }

        return tokens;
    }

    private static bool IsWordStart(char c) => char.IsLetter(c);

    /// <summary>
    /// Avança até o fim da palavra. Apóstrofo conta como interno quando há letra dos dois
    /// lados -- é o que separa <c>don't</c> e <c>O'Brien</c> de <c>'uma citação'</c>.
    /// <para>
    /// Hífen <b>não</b> junta, de propósito: <c>well-known</c> vira duas palavras. Juntar
    /// seria mais correto em teoria, mas o dicionário en_US não tem a maioria dos compostos
    /// hifenizados válidos, e cada ausência viraria um sublinhado falso -- que é o pior
    /// defeito possível num corretor.
    /// </para>
    /// </summary>
    private static int ScanWord(string text, int index)
    {
        index++;

        while (index < text.Length)
        {
            var c = text[index];

            if (char.IsLetter(c))
            {
                index++;
                continue;
            }

            // O sublinhado junta para que "user_id" chegue inteiro em LooksLikeCode e seja
            // descartado como identificador. Sem isto o tokenizador quebrava antes, em
            // "user" e "id", e a verificação de snake_case era inalcançável.
            var isInternalJoiner = (c is '\'' or '’' or '_')
                && index + 1 < text.Length
                && char.IsLetter(text[index + 1]);

            if (!isInternalJoiner)
            {
                break;
            }

            index += 2;
        }

        return index;
    }

    private static bool IsVerifiable(
        string word,
        int start,
        int length,
        List<(int Start, int End)> skipRanges,
        bool skipAllCaps)
    {
        // Letra sozinha não tem como estar errada de forma útil.
        if (word.Length < 2)
        {
            return false;
        }

        // Sobrepõe uma URL, e-mail, menção ou domínio.
        foreach (var range in skipRanges)
        {
            if (start < range.End && start + length > range.Start)
            {
                return false;
            }
        }

        if (skipAllCaps && IsAllUpper(word))
        {
            return false;
        }

        return !LooksLikeCode(word);
    }

    private static bool IsAllUpper(string word)
    {
        var sawLetter = false;

        foreach (var c in word)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            if (char.IsLower(c))
            {
                return false;
            }

            sawLetter = true;
        }

        return sawLetter;
    }

    /// <summary>
    /// Identificador de código, não palavra de texto: <c>snake_case</c>, <c>camelCase</c>,
    /// <c>PascalCase</c>. Descartar <c>iPhone</c> junto é um preço aceitável.
    /// </summary>
    private static bool LooksLikeCode(string word)
    {
        if (word.Contains('_', StringComparison.Ordinal))
        {
            return true;
        }

        for (var i = 1; i < word.Length; i++)
        {
            if (char.IsUpper(word[i]) && char.IsLower(word[i - 1]))
            {
                return true;
            }
        }

        return false;
    }
}
