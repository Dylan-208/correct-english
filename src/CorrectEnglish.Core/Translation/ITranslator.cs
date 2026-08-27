namespace CorrectEnglish.Core.Translation;

/// <summary>
/// Tradução de inglês para português.
/// <para>
/// Nenhum método lança por indisponibilidade: traduzir é acréscimo, e acréscimo não tem
/// direito de derrubar a correção. Falha vira <c>null</c>.
/// </para>
/// </summary>
public interface ITranslator
{
    /// <summary>Nome exibido no rodapé da janela.</summary>
    string Name { get; }

    /// <summary>
    /// Diz se o serviço responde agora. Usado apenas para informar o usuário —
    /// <see cref="TranslateAsync"/> não depende desta chamada.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Traduz, ou devolve <c>null</c> se não for possível.</summary>
    Task<string?> TranslateAsync(string text, CancellationToken cancellationToken = default);
}
