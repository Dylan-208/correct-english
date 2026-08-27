using System.Net;
using System.Text;
using CorrectEnglish.Core.Corrections;
using CorrectEnglish.Core.Grammar;
using CorrectEnglish.Core.Spelling;
using Xunit;

namespace CorrectEnglish.Core.Tests;

public sealed class GrammarCorrectionProviderTests
{
    private sealed class StubClient : ILanguageToolClient
    {
        private readonly IReadOnlyList<GrammarMatch> _matches;

        public StubClient(params GrammarMatch[] matches) => _matches = matches;

        public string? LastCheckedText { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<GrammarMatch>> CheckAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            LastCheckedText = text;
            return Task.FromResult(_matches);
        }
    }

    private static GrammarMatch Match(
        int offset,
        int length,
        string replacement,
        string ruleId = "SOME_RULE",
        string categoryId = "GRAMMAR",
        string issueType = "grammar",
        string message = "Mensagem da regra.")
        => new(offset, length, message, [replacement], ruleId, categoryId, issueType);

    [Fact]
    public async Task Aplica_a_sugestao_da_regra()
    {
        // "I has a car" -> troca "has" (deslocamento 2, tamanho 3) por "have"
        var provider = new GrammarCorrectionProvider(new StubClient(Match(2, 3, "have")));

        var result = await provider.CorrectAsync("I has a car", Tone.Neutral);

        Assert.Equal("I have a car", result.CorrectedText);
        Assert.Single(result.Corrections);
        Assert.Equal("has", result.Corrections[0].From);
        Assert.Equal("have", result.Corrections[0].To);
    }

    [Fact]
    public async Task Aplica_varias_sugestoes_sem_corromper_deslocamentos()
    {
        // "I has a apple" -> "has"@2..5 e "a"@8..9
        var provider = new GrammarCorrectionProvider(new StubClient(
            Match(2, 3, "have"),
            Match(6, 1, "an", ruleId: "EN_A_VS_AN")));

        var result = await provider.CorrectAsync("I has a apple", Tone.Neutral);

        Assert.Equal("I have an apple", result.CorrectedText);
        Assert.Equal(2, result.Corrections.Count);
    }

    [Fact]
    public async Task Descarta_o_corretor_ortografico_do_languagetool()
    {
        // A camada L0 e dona de ortografia, e o tokenizador dela sabe nao sublinhar codigo.
        var provider = new GrammarCorrectionProvider(new StubClient(
            Match(0, 8, "received", ruleId: "MORFOLOGIK_RULE_EN_US", categoryId: "TYPOS")));

        var result = await provider.CorrectAsync("recieved", Tone.Neutral);

        Assert.Empty(result.Corrections);
        Assert.Equal("recieved", result.CorrectedText);
    }

    [Fact]
    public async Task Mantem_erro_de_artigo_mesmo_rotulado_como_misspelling()
    {
        // Bug real encontrado testando contra o servidor: o LanguageTool marca EN_A_VS_AN
        // como issueType "misspelling" e categoria "MISC", apesar de ser erro de artigo.
        // Filtrar por esses campos descartava a classe de erro que a L1 existe para pegar.
        var provider = new GrammarCorrectionProvider(new StubClient(
            Match(6, 1, "an", ruleId: "EN_A_VS_AN", categoryId: "MISC", issueType: "misspelling")));

        var result = await provider.CorrectAsync("I saw a apple", Tone.Neutral);

        var correction = Assert.Single(result.Corrections);
        Assert.Equal(CorrectionKind.Article, correction.Kind);
        Assert.Equal("I saw an apple", result.CorrectedText);
    }

    [Fact]
    public async Task Descarta_problemas_que_se_sobrepoem()
    {
        // Duas regras no mesmo trecho: aplicar as duas corromperia o texto, porque a
        // segunda usaria deslocamentos de antes da primeira.
        var provider = new GrammarCorrectionProvider(new StubClient(
            Match(2, 3, "have"),
            Match(3, 3, "having")));

        var result = await provider.CorrectAsync("I has a car", Tone.Neutral);

        Assert.Single(result.Corrections);
        Assert.Equal("I have a car", result.CorrectedText);
    }

    [Theory]
    [InlineData("EN_A_VS_AN", "MISC", CorrectionKind.Article)]
    [InlineData("SOME_PREPOSITION_RULE", "GRAMMAR", CorrectionKind.Preposition)]
    [InlineData("VERB_AGREEMENT_X", "GRAMMAR", CorrectionKind.VerbTense)]
    [InlineData("PAST_TENSE_THING", "GRAMMAR", CorrectionKind.VerbTense)]
    [InlineData("WHATEVER", "PUNCTUATION", CorrectionKind.Punctuation)]
    [InlineData("WHATEVER", "STYLE", CorrectionKind.Naturalness)]
    [InlineData("WHATEVER", "REDUNDANCY", CorrectionKind.Naturalness)]
    [InlineData("WHATEVER", "SOMETHING_ELSE", CorrectionKind.Other)]
    public async Task Classifica_o_tipo_do_erro(string ruleId, string categoryId, CorrectionKind expected)
    {
        var provider = new GrammarCorrectionProvider(new StubClient(
            Match(0, 1, "x", ruleId: ruleId, categoryId: categoryId)));

        var result = await provider.CorrectAsync("abc", Tone.Neutral);

        Assert.Equal(expected, Assert.Single(result.Corrections).Kind);
    }

    [Fact]
    public async Task Servidor_fora_do_ar_devolve_o_texto_intacto()
    {
        var provider = new GrammarCorrectionProvider(new StubClient());

        var result = await provider.CorrectAsync("I has a car", Tone.Neutral);

        Assert.Empty(result.Corrections);
        Assert.True(result.IsUnchanged);
    }

    [Fact]
    public async Task Sem_tradutor_a_explicacao_fica_em_ingles()
    {
        var provider = new GrammarCorrectionProvider(new StubClient(
            Match(2, 3, "have", message: "Possible agreement error.")));

        var result = await provider.CorrectAsync("I has a car", Tone.Neutral);

        Assert.Equal("Possible agreement error.", Assert.Single(result.Corrections).Why);
    }
}

public sealed class PipelineCorrectionProviderTests
{
    private sealed class ReplaceStage : ICorrectionProvider
    {
        private readonly string _from;
        private readonly string _to;

        public ReplaceStage(string name, string from, string to)
        {
            Name = name;
            _from = from;
            _to = to;
        }

        public string Name { get; }

        public bool RequiresNetwork => false;

        public int Calls { get; private set; }

        public string? SawText { get; private set; }

        public Task<CorrectionResult> CorrectAsync(
            string text,
            Tone tone,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            SawText = text;

            var corrected = text.Replace(_from, _to, StringComparison.Ordinal);

            return Task.FromResult(new CorrectionResult
            {
                OriginalText = text,
                CorrectedText = corrected,
                EngineName = Name,
                Corrections = corrected == text
                    ? []
                    : [new Correction(_from, _to, CorrectionKind.Other, $"{Name} trocou.")],
            });
        }
    }

    [Fact]
    public async Task Encadeia_a_saida_de_uma_camada_na_entrada_da_seguinte()
    {
        var first = new ReplaceStage("L0", "reprot", "report");
        var second = new ReplaceStage("L1", "has", "have");

        var pipeline = new PipelineCorrectionProvider(first, second);
        var result = await pipeline.CorrectAsync("he has the reprot", Tone.Neutral);

        Assert.Equal("he has the reprot", first.SawText);
        Assert.Equal("he has the report", second.SawText); // ja corrigido pela L0
        Assert.Equal("he have the report", result.CorrectedText);
    }

    [Fact]
    public async Task Acumula_as_correcoes_de_todas_as_camadas()
    {
        var pipeline = new PipelineCorrectionProvider(
            new ReplaceStage("L0", "reprot", "report"),
            new ReplaceStage("L1", "has", "have"));

        var result = await pipeline.CorrectAsync("he has the reprot", Tone.Neutral);

        Assert.Equal(2, result.Corrections.Count);
        Assert.Contains(result.Corrections, c => c.Why.StartsWith("L0", StringComparison.Ordinal));
        Assert.Contains(result.Corrections, c => c.Why.StartsWith("L1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preserva_o_texto_original_e_nao_o_intermediario()
    {
        var pipeline = new PipelineCorrectionProvider(
            new ReplaceStage("L0", "reprot", "report"),
            new ReplaceStage("L1", "has", "have"));

        var result = await pipeline.CorrectAsync("he has the reprot", Tone.Neutral);

        Assert.Equal("he has the reprot", result.OriginalText);
    }

    [Fact]
    public async Task Nome_lista_todas_as_camadas()
    {
        var pipeline = new PipelineCorrectionProvider(
            new ReplaceStage("Hunspell en_US", "a", "a"),
            new ReplaceStage("LanguageTool", "a", "a"));

        Assert.Equal("Hunspell en_US + LanguageTool", pipeline.Name);

        var result = await pipeline.CorrectAsync("abc", Tone.Neutral);
        Assert.Equal("Hunspell en_US + LanguageTool", result.EngineName);
    }

    [Fact]
    public void Pipeline_vazio_e_rejeitado()
        => Assert.Throws<ArgumentException>(() => new PipelineCorrectionProvider());

    [Fact]
    public async Task Uma_camada_indisponivel_nao_impede_as_outras()
    {
        var pipeline = new PipelineCorrectionProvider(
            new SpellingCorrectionProvider(
                HunspellSpellChecker.FromWords(["the", "report", "send"], "teste")),
            new UnavailableCorrectionProvider("Gramatica desligada."));

        var result = await pipeline.CorrectAsync("send the reprot", Tone.Neutral);

        Assert.Equal("send the report", result.CorrectedText);
        Assert.Contains(result.Corrections, c => c.Why == "Gramatica desligada.");
    }
}

public sealed class LanguageToolClientTests
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

        public string? LastContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                LastContentType = request.Content.Headers.ContentType?.MediaType;
            }

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static LanguageToolClient Client(StubHandler handler)
        => new(
            new Uri("http://localhost:8010"),
            httpClient: new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8010") });

    [Fact]
    public async Task Le_os_problemas_da_resposta()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
        {
          "matches": [
            {
              "message": "Possible agreement error.",
              "offset": 2,
              "length": 3,
              "replacements": [{"value": "have"}, {"value": "had"}],
              "rule": {
                "id": "BASE_FORM",
                "issueType": "grammar",
                "category": {"id": "GRAMMAR"}
              }
            }
          ]
        }
        """);

        var matches = await Client(handler).CheckAsync("I has a car");

        var match = Assert.Single(matches);
        Assert.Equal(2, match.Offset);
        Assert.Equal(3, match.Length);
        Assert.Equal("BASE_FORM", match.RuleId);
        Assert.Equal("GRAMMAR", match.CategoryId);
        Assert.Equal(["have", "had"], match.Replacements);
    }

    [Fact]
    public async Task Envia_form_encoded_e_nao_json()
    {
        // A API v2/check recusa JSON com 400. Este teste existe para o formato nao
        // regredir numa refatoracao que "modernize" para PostAsJsonAsync.
        var handler = new StubHandler(HttpStatusCode.OK, """{"matches":[]}""");

        await Client(handler).CheckAsync("hello");

        Assert.Equal("application/x-www-form-urlencoded", handler.LastContentType);
        Assert.Contains("text=hello", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("language=en-US", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resposta_sem_problemas_devolve_lista_vazia()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"matches":[]}""");
        Assert.Empty(await Client(handler).CheckAsync("all good here"));
    }

    [Fact]
    public async Task Servidor_com_erro_devolve_lista_vazia_em_vez_de_lancar()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "boom");
        Assert.Empty(await Client(handler).CheckAsync("hello"));
    }

    [Fact]
    public async Task Resposta_malformada_devolve_lista_vazia_em_vez_de_lancar()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "isto nao e json");
        Assert.Empty(await Client(handler).CheckAsync("hello"));
    }

    [Fact]
    public async Task Texto_vazio_nao_chega_a_chamar_o_servidor()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"matches":[]}""");

        Assert.Empty(await Client(handler).CheckAsync("   "));
        Assert.Null(handler.LastRequestBody);
    }
}
