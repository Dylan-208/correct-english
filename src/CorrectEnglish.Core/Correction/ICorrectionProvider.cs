namespace CorrectEnglish.Core.Correction;

/// <summary>
/// Contrato unico para as tres camadas do ADR 0002. A janela nao sabe (nem precisa saber)
/// se o resultado veio do dicionario local, do LanguageTool ou do Claude.
/// </summary>
public interface ICorrectionProvider
{
    /// <summary>Nome exibido no rodape da janela.</summary>
    string Name { get; }

    /// <summary>True quando o motor precisa de rede para funcionar.</summary>
    bool RequiresNetwork { get; }

    Task<CorrectionResult> CorrectAsync(
        string text,
        Tone tone,
        CancellationToken cancellationToken = default);
}
