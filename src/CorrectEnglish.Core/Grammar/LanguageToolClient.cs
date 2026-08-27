using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CorrectEnglish.Core.Grammar;

/// <summary>Um problema apontado pelo LanguageTool, já achatado para o que nos interessa.</summary>
/// <param name="Offset">Início do trecho problemático no texto enviado.</param>
/// <param name="Length">Tamanho do trecho.</param>
/// <param name="Message">Explicação da regra, em inglês.</param>
/// <param name="Replacements">Substituições sugeridas, da mais provável para a menos.</param>
/// <param name="RuleId">Identificador da regra, ex.: <c>EN_A_VS_AN</c>.</param>
/// <param name="CategoryId">Categoria da regra, ex.: <c>GRAMMAR</c>, <c>TYPOS</c>.</param>
/// <param name="IssueType">Tipo, ex.: <c>grammar</c>, <c>misspelling</c>, <c>style</c>.</param>
public sealed record GrammarMatch(
    int Offset,
    int Length,
    string Message,
    IReadOnlyList<string> Replacements,
    string RuleId,
    string CategoryId,
    string IssueType);

public interface ILanguageToolClient
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Analisa o texto. Devolve lista vazia quando não há problema <b>ou</b> quando o
    /// servidor não responde — pela mesma razão do tradutor: gramática é acréscimo,
    /// e acréscimo não derruba o principal.
    /// </summary>
    Task<IReadOnlyList<GrammarMatch>> CheckAsync(
        string text,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Cliente do LanguageTool rodando em <c>localhost</c> (ver
/// <see href="../../../docs/adr/0006-gramatica-com-languagetool.md">ADR 0006</see>).
/// </summary>
public sealed class LanguageToolClient : ILanguageToolClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _language;

    public LanguageToolClient(
        Uri baseAddress,
        string language = "en-US",
        HttpClient? httpClient = null)
    {
        _language = language;
        _ownsHttpClient = httpClient is null;

        _http = httpClient ?? new HttpClient
        {
            // A primeira chamada depois da subida do contêiner carrega os modelos de
            // linguagem e pode passar de 10 s. Depois fica na casa dos 100 ms.
            Timeout = TimeSpan.FromSeconds(25),
        };

        _http.BaseAddress ??= baseAddress;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));

            using var response = await _http
                .GetAsync("/v2/languages", timeout.Token)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<GrammarMatch>> CheckAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<GrammarMatch>();
        }

        try
        {
            // A API v2/check é form-encoded, não JSON. Enviar JSON devolve 400.
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = text,
                ["language"] = _language,
            });

            using var response = await _http
                .PostAsync("/v2/check", content, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<GrammarMatch>();
            }

            var payload = await response.Content
                .ReadFromJsonAsync<CheckResponse>(cancellationToken)
                .ConfigureAwait(false);

            if (payload?.Matches is null)
            {
                return Array.Empty<GrammarMatch>();
            }

            return payload.Matches
                .Where(m => m.Offset >= 0 && m.Length > 0)
                .Select(m => new GrammarMatch(
                    Offset: m.Offset,
                    Length: m.Length,
                    Message: m.Message ?? string.Empty,
                    Replacements: m.Replacements?
                        .Select(r => r.Value)
                        .Where(v => !string.IsNullOrEmpty(v))
                        .Select(v => v!)
                        .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>(),
                    RuleId: m.Rule?.Id ?? string.Empty,
                    CategoryId: m.Rule?.Category?.Id ?? string.Empty,
                    IssueType: m.Rule?.IssueType ?? string.Empty))
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<GrammarMatch>();
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private sealed record CheckResponse
    {
        [JsonPropertyName("matches")]
        public List<MatchDto>? Matches { get; init; }
    }

    private sealed record MatchDto
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("offset")]
        public int Offset { get; init; }

        [JsonPropertyName("length")]
        public int Length { get; init; }

        [JsonPropertyName("replacements")]
        public List<ReplacementDto>? Replacements { get; init; }

        [JsonPropertyName("rule")]
        public RuleDto? Rule { get; init; }
    }

    private sealed record ReplacementDto
    {
        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }

    private sealed record RuleDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("issueType")]
        public string? IssueType { get; init; }

        [JsonPropertyName("category")]
        public CategoryDto? Category { get; init; }
    }

    private sealed record CategoryDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }
}
