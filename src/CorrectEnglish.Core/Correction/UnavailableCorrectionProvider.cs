namespace CorrectEnglish.Core.Correction;

/// <summary>
/// Provedor que não corrige nada e explica por quê.
/// <para>
/// Usado quando o dicionário não foi encontrado. Existe para que a falha apareça na
/// própria janela, com instrução do que fazer, em vez de virar um caso especial espalhado
/// pelo código do aplicativo — a janela já sabe desabilitar o Replace quando o texto não
/// muda, então nada mais precisa saber que este caso existe.
/// </para>
/// </summary>
public sealed class UnavailableCorrectionProvider : ICorrectionProvider
{
    private readonly string _reason;

    public UnavailableCorrectionProvider(string reason) => _reason = reason;

    public string Name => "indisponível";

    public bool RequiresNetwork => false;

    public Task<CorrectionResult> CorrectAsync(
        string text,
        Tone tone,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new CorrectionResult
        {
            OriginalText = text,
            CorrectedText = text,
            Tone = tone,
            EngineName = Name,
            Confidence = 0,
            Corrections =
            [
                new Correction(
                    From: string.Empty,
                    To: string.Empty,
                    Kind: CorrectionKind.Other,
                    Why: _reason),
            ],
        });
}
