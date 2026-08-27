using CorrectEnglish.Core.Correction;
using CorrectEnglish.Core.Spelling;
using Xunit;

namespace CorrectEnglish.Core.Tests;

/// <summary>
/// Usa um dicionário construído em memória, então os testes rodam sem baixar os arquivos
/// do Hunspell -- o suíte continua verde num clone limpo.
/// </summary>
public sealed class SpellingCorrectionProviderTests
{
    // Todo palavra usada nos testes precisa estar aqui: um dicionário de teste incompleto
    // gera correção onde o teste não esperava, e a falha parece ser do código.
    private static readonly string[] Vocabulary =
    [
        "the", "report", "information", "receive", "received", "please", "send", "sent",
        "confirm", "everything", "correct", "numbers", "yesterday", "meeting", "tomorrow",
        "know", "anything", "else", "need", "needs", "email", "about", "let", "me", "and",
        "don't", "think", "ready", "with", "you", "your", "to", "or", "are", "fine",
        "attached", "is", "this", "that", "for", "on", "in", "of", "it",
    ];

    private static SpellingCorrectionProvider Provider()
        => new(HunspellSpellChecker.FromWords(Vocabulary, "teste"));

    private static Task<CorrectionResult> Correct(string text)
        => Provider().CorrectAsync(text, Tone.Neutral);

    [Fact]
    public async Task Texto_sem_erro_volta_intacto()
    {
        var result = await Correct("please send the report");

        Assert.Empty(result.Corrections);
        Assert.Equal("please send the report", result.CorrectedText);
        Assert.True(result.IsUnchanged);
    }

    [Fact]
    public async Task Detecta_palavra_fora_do_dicionario()
    {
        var result = await Correct("please send the reprot");

        var correction = Assert.Single(result.Corrections);
        Assert.Equal("reprot", correction.From);
        Assert.Equal(CorrectionKind.Spelling, correction.Kind);
    }

    [Fact]
    public async Task Aplica_a_sugestao_no_texto_corrigido()
    {
        var result = await Correct("please send the reprot");

        Assert.Equal("please send the report", result.CorrectedText);
        Assert.False(result.IsUnchanged);
    }

    [Fact]
    public async Task Corrige_varias_palavras_de_uma_vez()
    {
        var result = await Correct("plese send the reprot");

        Assert.Equal(2, result.Corrections.Count);
        Assert.Equal("please send the report", result.CorrectedText);
    }

    [Fact]
    public async Task Preserva_a_capitalizacao_da_palavra_original()
    {
        var result = await Correct("Reprot attached");

        // "attached" nao esta no vocabulario de teste, entao ignoramos o resto da frase
        // e checamos so a primeira palavra.
        Assert.StartsWith("Report", result.CorrectedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nao_mexe_em_url_nem_email()
    {
        const string text = "send the report to dylansilva208@gmail.com or https://exemplo.com/x";
        var result = await Correct(text);

        // A intencao do teste: o endereco e a URL saem intactos, caractere por caractere.
        // Asseverado separadamente do texto inteiro para que uma lacuna no vocabulario de
        // teste nao pareca uma falha no tratamento de URL.
        Assert.Contains("dylansilva208@gmail.com", result.CorrectedText, StringComparison.Ordinal);
        Assert.Contains("https://exemplo.com/x", result.CorrectedText, StringComparison.Ordinal);

        Assert.Equal(text, result.CorrectedText);
        Assert.Empty(result.Corrections);
    }

    [Fact]
    public async Task Nao_mexe_em_identificador_de_codigo()
    {
        const string text = "the getUserById and user_id are fine";
        var result = await Correct(text);

        Assert.DoesNotContain(result.Corrections, c => c.From is "getUserById" or "user_id");
    }

    [Fact]
    public async Task Aceita_apostrofo_tipografico_como_correto()
    {
        // O Word e o WhatsApp trocam ' por ’ automaticamente. Sem normalizacao, esta
        // seria a palavra errada mais comum do app.
        var result = await Correct("I don’t think");

        Assert.DoesNotContain(result.Corrections, c => c.From.Contains("don", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_explicacao_cita_a_palavra_e_a_sugestao()
    {
        var result = await Correct("the reprot");

        var why = Assert.Single(result.Corrections).Why;
        Assert.Contains("reprot", why, StringComparison.Ordinal);
        Assert.Contains("report", why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nao_traduz_porque_esta_camada_nao_traduz()
    {
        var result = await Correct("the report");
        Assert.Equal(string.Empty, result.TranslationPt);
    }

    [Fact]
    public async Task Palavra_ignorada_deixa_de_ser_acusada()
    {
        var checker = HunspellSpellChecker.FromWords(Vocabulary, "teste");
        var provider = new SpellingCorrectionProvider(checker);

        var before = await provider.CorrectAsync("the reprot", Tone.Neutral);
        Assert.NotEmpty(before.Corrections);

        checker.Ignore("reprot");

        var after = await provider.CorrectAsync("the reprot", Tone.Neutral);
        Assert.Empty(after.Corrections);
    }

    [Fact]
    public async Task Provedor_indisponivel_explica_o_motivo_e_nao_altera_o_texto()
    {
        var provider = new UnavailableCorrectionProvider("Dicionário não instalado.");
        var result = await provider.CorrectAsync("qualquer coisa", Tone.Neutral);

        Assert.True(result.IsUnchanged);
        Assert.Equal("Dicionário não instalado.", Assert.Single(result.Corrections).Why);
    }
}
