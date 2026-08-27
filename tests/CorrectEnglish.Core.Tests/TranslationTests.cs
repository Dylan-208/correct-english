using System.Net;
using System.Text;
using CorrectEnglish.Core.Corrections;
using CorrectEnglish.Core.Spelling;
using CorrectEnglish.Core.Translation;
using Xunit;

namespace CorrectEnglish.Core.Tests;

public sealed class TranslatingCorrectionProviderTests
{
    private static readonly string[] Vocabulary =
        ["the", "report", "please", "send", "is", "ready"];

    private static ICorrectionProvider Spelling()
        => new SpellingCorrectionProvider(HunspellSpellChecker.FromWords(Vocabulary, "teste"));

    private sealed class StubTranslator : ITranslator
    {
        private readonly Func<string, string?> _behaviour;

        public StubTranslator(Func<string, string?> behaviour) => _behaviour = behaviour;

        public string Name => "Stub";

        public string? LastRequestedText { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string?> TranslateAsync(string text, CancellationToken cancellationToken = default)
        {
            LastRequestedText = text;
            return Task.FromResult(_behaviour(text));
        }
    }

    [Fact]
    public async Task Acrescenta_a_traducao_ao_resultado()
    {
        var provider = new TranslatingCorrectionProvider(
            Spelling(),
            new StubTranslator(_ => "Por favor envie o relatório"));

        var result = await provider.CorrectAsync("please send the report", Tone.Neutral);

        Assert.Equal("Por favor envie o relatório", result.TranslationPt);
        Assert.Contains("Stub", result.EngineName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Traduz_o_texto_corrigido_e_nao_o_original()
    {
        // Erro de digitação degrada traducao automatica de forma desproporcional, entao a
        // traducao tem que receber a frase ja arrumada. Ver ADR 0005.
        var translator = new StubTranslator(_ => "irrelevante");
        var provider = new TranslatingCorrectionProvider(Spelling(), translator);

        await provider.CorrectAsync("please send the reprot", Tone.Neutral);

        Assert.Equal("please send the report", translator.LastRequestedText);
    }

    [Fact]
    public async Task Traducao_indisponivel_nao_impede_a_correcao()
    {
        var provider = new TranslatingCorrectionProvider(
            Spelling(),
            new StubTranslator(_ => null));

        var result = await provider.CorrectAsync("please send the reprot", Tone.Neutral);

        Assert.Equal(string.Empty, result.TranslationPt);
        Assert.Equal("please send the report", result.CorrectedText);
        Assert.NotEmpty(result.Corrections);

        // O rodape nao deve creditar um tradutor que nao traduziu.
        Assert.DoesNotContain("Stub", result.EngineName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Excecao_no_tradutor_nao_derruba_a_correcao()
    {
        var provider = new TranslatingCorrectionProvider(
            Spelling(),
            new StubTranslator(_ => throw new InvalidOperationException("servidor explodiu")));

        var result = await provider.CorrectAsync("please send the reprot", Tone.Neutral);

        Assert.Equal("please send the report", result.CorrectedText);
        Assert.Equal(string.Empty, result.TranslationPt);
    }

    [Fact]
    public async Task Cancelamento_do_chamador_continua_propagando()
    {
        // Degradar por falha do tradutor e correto; engolir um cancelamento pedido pelo
        // usuario nao e -- seria o app ignorando um Esc.
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var provider = new TranslatingCorrectionProvider(
            Spelling(),
            new StubTranslator(_ => "nunca chega aqui"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.CorrectAsync("the report", Tone.Neutral, cancellation.Token));
    }
}

public sealed class LibreTranslateTranslatorTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static LibreTranslateTranslator Translator(StubHandler handler)
        => new(
            new Uri("http://localhost:5000"),
            apiKey: null,
            httpClient: new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") });

    [Fact]
    public async Task Le_a_traducao_da_resposta()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """{"translatedText":"Por favor envie o relatório"}""");

        var result = await Translator(handler).TranslateAsync("please send the report");

        Assert.Equal("Por favor envie o relatório", result);
    }

    [Fact]
    public async Task Envia_o_par_de_idiomas_correto()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"translatedText":"olá"}""");

        await Translator(handler).TranslateAsync("hello");

        Assert.Contains("\"source\":\"en\"", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"target\":\"pt\"", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"q\":\"hello\"", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Omite_a_chave_de_api_quando_nao_ha_chave()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"translatedText":"olá"}""");

        await Translator(handler).TranslateAsync("hello");

        Assert.DoesNotContain("api_key", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Servidor_com_erro_devolve_null_em_vez_de_lancar()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, "{}");

        Assert.Null(await Translator(handler).TranslateAsync("hello"));
    }

    [Fact]
    public async Task Resposta_malformada_devolve_null_em_vez_de_lancar()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "isto nao e json");

        Assert.Null(await Translator(handler).TranslateAsync("hello"));
    }

    [Fact]
    public async Task Resposta_sem_texto_devolve_null()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"translatedText":""}""");

        Assert.Null(await Translator(handler).TranslateAsync("hello"));
    }

    [Fact]
    public async Task Texto_vazio_nao_chega_a_chamar_o_servidor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"translatedText":"x"}""");

        Assert.Null(await Translator(handler).TranslateAsync("   "));
        Assert.Null(handler.LastRequestBody);
    }
}
