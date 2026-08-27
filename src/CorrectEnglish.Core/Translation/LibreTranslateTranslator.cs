using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CorrectEnglish.Core.Translation;

/// <summary>
/// Cliente do LibreTranslate rodando em <c>localhost</c> (ver
/// <see href="../../../docs/adr/0005-traducao-com-libretranslate.md">ADR 0005</see>).
/// </summary>
public sealed class LibreTranslateTranslator : ITranslator, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string? _apiKey;

    /// <param name="baseAddress">Endereço do servidor, ex.: <c>http://localhost:5000</c>.</param>
    /// <param name="apiKey">Só necessário se o servidor exigir chave.</param>
    /// <param name="httpClient">
    /// Injetável para teste. Quando fornecido, o chamador continua responsável por descartá-lo.
    /// </param>
    public LibreTranslateTranslator(
        Uri baseAddress,
        string? apiKey = null,
        HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _ownsHttpClient = httpClient is null;

        _http = httpClient ?? new HttpClient
        {
            // Generoso de proposito: a primeira chamada depois de o contêiner subir
            // carrega o modelo em memória e pode passar de 10 s.
            Timeout = TimeSpan.FromSeconds(25),
        };

        _http.BaseAddress ??= baseAddress;
    }

    public string Name => "LibreTranslate";

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));

            using var response = await _http
                .GetAsync("/languages", timeout.Token)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            // Contêiner desligado, porta fechada, DNS, timeout: tudo é "indisponível".
            return false;
        }
    }

    public async Task<string?> TranslateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var request = new TranslateRequest
            {
                Text = text,
                Source = "en",
                Target = "pt",
                ApiKey = _apiKey,
            };

            using var response = await _http
                .PostAsJsonAsync("/translate", request, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<TranslateResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(payload?.TranslatedText)
                ? null
                : payload.TranslatedText;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private sealed record TranslateRequest
    {
        [JsonPropertyName("q")]
        public required string Text { get; init; }

        [JsonPropertyName("source")]
        public required string Source { get; init; }

        [JsonPropertyName("target")]
        public required string Target { get; init; }

        [JsonPropertyName("format")]
        public string Format { get; init; } = "text";

        [JsonPropertyName("api_key")]
        public string? ApiKey { get; init; }
    }

    private sealed record TranslateResponse
    {
        [JsonPropertyName("translatedText")]
        public string? TranslatedText { get; init; }
    }
}
