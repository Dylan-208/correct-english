namespace CorrectEnglish.Core.Spelling;

/// <summary>
/// Camada L0 do <see href="../../../docs/adr/0002-motor-de-correcao.md">ADR 0002</see>:
/// ortografia, em memória, sem rede e sem custo.
/// </summary>
public interface ISpellChecker
{
    /// <summary>Nome do dicionário carregado, para exibir na interface.</summary>
    string Name { get; }

    bool IsCorrect(string word);

    /// <summary>Sugestões ordenadas da mais provável para a menos.</summary>
    IReadOnlyList<string> Suggest(string word, int max = 3);

    /// <summary>
    /// Marca uma palavra como aceitável nesta sessão. Alimenta o botão
    /// "adicionar ao dicionário"; a persistência em disco vem depois.
    /// </summary>
    void Ignore(string word);
}
