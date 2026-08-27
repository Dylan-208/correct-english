using CorrectEnglish.Core.Corrections;
using CorrectEnglish.Core.Grammar;
using CorrectEnglish.Core.Spelling;
using CorrectEnglish.Core.Translation;
using Xunit;
using Xunit.Abstractions;

namespace CorrectEnglish.Core.Tests;

/// <summary>
/// Exercita o LanguageTool de verdade em <c>localhost:8010</c>. Sai sem asseverar nada
/// quando o contêiner não está de pé — o suíte não pode depender de Docker.
/// </summary>
public sealed class RealGrammarTests
{
    private readonly ITestOutputHelper _output;

    public RealGrammarTests(ITestOutputHelper output) => _output = output;

    private async Task<GrammarCorrectionProvider?> TryCreate(ITranslator? translator = null)
    {
        var client = new LanguageToolClient(new Uri("http://localhost:8010"));

        if (!await client.IsAvailableAsync())
        {
            _output.WriteLine("LanguageTool fora do ar; rode \"docker compose up -d\".");
            client.Dispose();
            return null;
        }

        return new GrammarCorrectionProvider(client, translator);
    }

    private void Dump(CorrectionResult result)
    {
        _output.WriteLine($"corrigido: {result.CorrectedText}");

        foreach (var correction in result.Corrections)
        {
            _output.WriteLine($"  [{correction.Kind}] {correction.From} -> {correction.To}");
            _output.WriteLine($"      {correction.Why}");
        }
    }

    [Fact]
    public async Task Pega_concordancia_verbal()
    {
        var provider = await TryCreate();
        if (provider is null)
        {
            return;
        }

        var result = await provider.CorrectAsync("I has a car and she dont know.", Tone.Neutral);
        Dump(result);

        Assert.NotEmpty(result.Corrections);
        Assert.Contains("have", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O caso que motivou o ajuste do filtro: o LanguageTool rotula EN_A_VS_AN como
    /// <c>issueType: misspelling</c>, categoria <c>MISC</c>. Filtrar por esses campos
    /// descartava erro de artigo, que é exatamente o que a camada L1 existe para pegar.
    /// </summary>
    [Fact]
    public async Task Pega_erro_de_artigo_apesar_do_rotulo_enganoso()
    {
        var provider = await TryCreate();
        if (provider is null)
        {
            return;
        }

        var result = await provider.CorrectAsync("She found a apple on the table.", Tone.Neutral);
        Dump(result);

        Assert.Contains("an apple", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Corrections, c => c.Kind == CorrectionKind.Article);
    }

    /// <summary>
    /// O teste mais importante deste arquivo. Descartamos apenas a família MORFOLOGIK do
    /// LanguageTool, apostando que as outras regras não disparam em código, URL e e-mail.
    /// Se a aposta estiver errada, o app sublinha coisa certa — o defeito que destrói a
    /// confiança do usuário mais rápido que qualquer erro não detectado.
    /// </summary>
    [Fact]
    public async Task Nao_produz_falso_positivo_em_texto_tecnico_real()
    {
        var provider = await TryCreate();
        if (provider is null)
        {
            return;
        }

        const string text =
            "Please check https://github.com/Dylan-208/correct-english and call the "
            + "getUserById method. Send the result to dylansilva208@gmail.com or ping "
            + "@sarah about it. I don't think the API needs user_id for that.";

        var result = await provider.CorrectAsync(text, Tone.Neutral);
        Dump(result);

        Assert.Equal(text, result.CorrectedText);
        Assert.Empty(result.Corrections);
    }

    [Fact]
    public async Task Frase_correta_nao_gera_nenhuma_correcao()
    {
        var provider = await TryCreate();
        if (provider is null)
        {
            return;
        }

        var result = await provider.CorrectAsync(
            "I sent you the report yesterday and everything looks correct.",
            Tone.Neutral);
        Dump(result);

        Assert.Empty(result.Corrections);
    }

    [Fact]
    public async Task Explicacao_e_traduzida_para_portugues_quando_ha_tradutor()
    {
        using var translator = new LibreTranslateTranslator(new Uri("http://localhost:5000"));

        if (!await translator.IsAvailableAsync())
        {
            _output.WriteLine("LibreTranslate fora do ar; teste sem assercao.");
            return;
        }

        var provider = await TryCreate(translator);
        if (provider is null)
        {
            return;
        }

        var result = await provider.CorrectAsync("I has a car.", Tone.Neutral);
        Dump(result);

        var why = Assert.Single(result.Corrections).Why;

        // A mensagem original do LanguageTool para esta regra é
        // "Possible agreement error - use the base form here."
        Assert.DoesNotContain("Possible agreement", why, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O pipeline completo, como o app monta: ortografia, depois gramática, depois tradução.
    /// </summary>
    [Fact]
    public async Task Pipeline_completo_corrige_ortografia_e_gramatica_na_mesma_frase()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? dictionary = null;
        string? affix = null;

        for (var depth = 0; depth < 8 && directory is not null; depth++)
        {
            var candidateDic = Path.Combine(directory.FullName, "assets", "dictionaries", "en_US.dic");
            var candidateAff = Path.Combine(directory.FullName, "assets", "dictionaries", "en_US.aff");

            if (File.Exists(candidateDic) && File.Exists(candidateAff))
            {
                dictionary = candidateDic;
                affix = candidateAff;
                break;
            }

            directory = directory.Parent;
        }

        if (dictionary is null || affix is null)
        {
            _output.WriteLine("Dicionário ausente; teste sem assercao.");
            return;
        }

        var grammar = await TryCreate();
        if (grammar is null)
        {
            return;
        }

        var pipeline = new PipelineCorrectionProvider(
            new SpellingCorrectionProvider(
                HunspellSpellChecker.FromFiles(dictionary, affix, "en_US")),
            grammar);

        // "recieved" e erro de ortografia (L0); "has" e concordancia (L1).
        var result = await pipeline.CorrectAsync("She has recieved a apple.", Tone.Neutral);
        Dump(result);

        Assert.Contains("received", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("an apple", result.CorrectedText, StringComparison.OrdinalIgnoreCase);

        // Prova que as duas camadas contribuiram.
        Assert.Contains(result.Corrections, c => c.Kind == CorrectionKind.Spelling);
        Assert.Contains(result.Corrections, c => c.Kind == CorrectionKind.Article);
    }
}
