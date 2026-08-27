namespace CorrectEnglish.Core.Correction;

/// <summary>Registro em que o texto corrigido deve ser escrito.</summary>
public enum Tone
{
    Neutral,
    Formal,
    Informal,
}

/// <summary>
/// Categoria do erro. Espelha o campo <c>tipo</c> do schema definido no ADR 0002,
/// para que as tres camadas (ortografia, gramatica, IA) produzam a mesma taxonomia.
/// </summary>
public enum CorrectionKind
{
    Spelling,
    VerbTense,
    Preposition,
    Article,
    Naturalness,
    Punctuation,
    Other,
}

/// <summary>Uma alteracao pontual, com a explicacao em portugues.</summary>
/// <param name="From">Trecho original.</param>
/// <param name="To">Trecho corrigido.</param>
/// <param name="Kind">Categoria do erro.</param>
/// <param name="Why">Explicacao em portugues, uma frase.</param>
public sealed record Correction(
    string From,
    string To,
    CorrectionKind Kind,
    string Why);

/// <summary>
/// Resultado unificado das tres camadas. A camada L0 (ortografia) preenche apenas
/// <see cref="Corrections"/>; a L2 (IA) preenche todos os campos.
/// </summary>
public sealed record CorrectionResult
{
    public required string OriginalText { get; init; }

    /// <summary>Traducao para portugues. Vazio quando a camada nao traduz.</summary>
    public string TranslationPt { get; init; } = string.Empty;

    public required string CorrectedText { get; init; }

    public Tone Tone { get; init; } = Tone.Neutral;

    public IReadOnlyList<Correction> Corrections { get; init; } = Array.Empty<Correction>();

    /// <summary>Outras formas de escrever a mesma frase.</summary>
    public IReadOnlyList<string> Alternatives { get; init; } = Array.Empty<string>();

    public double Confidence { get; init; } = 1.0;

    /// <summary>Nome do motor que produziu o resultado, para exibir no rodape da janela.</summary>
    public required string EngineName { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>True quando o motor nao encontrou nada para mudar.</summary>
    public bool IsUnchanged =>
        string.Equals(OriginalText.Trim(), CorrectedText.Trim(), StringComparison.Ordinal);
}
