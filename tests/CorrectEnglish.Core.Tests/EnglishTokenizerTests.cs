using CorrectEnglish.Core.Spelling;
using Xunit;

namespace CorrectEnglish.Core.Tests;

/// <summary>
/// O tokenizador decide o que o corretor tem permissão de sublinhar. Um falso positivo aqui
/// é pior que um erro não detectado: sublinhado errado destrói a confiança no app. Estes
/// testes protegem principalmente o que NÃO deve ser verificado.
/// </summary>
public sealed class EnglishTokenizerTests
{
    private static string[] Words(string text)
        => EnglishTokenizer.Tokenize(text).Select(t => t.Text).ToArray();

    [Fact]
    public void Extrai_palavras_simples()
        => Assert.Equal(["Please", "send", "the", "report"], Words("Please send the report."));

    [Fact]
    public void Descarta_url()
        => Assert.Equal(["Check", "this"], Words("Check https://example.com/some/page this"));

    [Fact]
    public void Descarta_url_sem_esquema()
        => Assert.Equal(["See"], Words("See www.microsoft.com"));

    [Fact]
    public void Descarta_email()
        => Assert.Equal(["Mail", "me"], Words("Mail me dylansilva208@gmail.com"));

    [Fact]
    public void Descarta_mencao_e_hashtag()
        => Assert.Equal(["and"], Words("@sarah and #urgent"));

    [Fact]
    public void Descarta_camel_case_e_pascal_case()
        => Assert.Equal(["The", "method", "returns"], Words("The getUserById method returns"));

    [Fact]
    public void Descarta_snake_case()
        => Assert.Equal(["Use", "instead"], Words("Use user_id instead"));

    [Fact]
    public void Descarta_maiusculas_por_serem_siglas()
        => Assert.Equal(["The", "returns", "json"], Words("The API returns json"));

    [Fact]
    public void Mantem_maiusculas_quando_pedido()
        => Assert.Equal(["The", "API", "is", "fine"],
            EnglishTokenizer.Tokenize("The API is fine", skipAllCaps: false)
                .Select(t => t.Text).ToArray());

    [Fact]
    public void Descarta_letra_sozinha()
        => Assert.Equal(["went", "there"], Words("I went there"));

    [Fact]
    public void Descarta_numeros_e_versoes()
        => Assert.Equal(["Release", "on"], Words("Release 2.0 on 15/08"));

    [Fact]
    public void Mantem_apostrofo_reto_dentro_da_palavra()
        => Assert.Equal(["don't", "think", "it's", "ready"], Words("I don't think it's ready"));

    [Fact]
    public void Mantem_apostrofo_tipografico()
        => Assert.Equal(["don’t", "worry"], Words("I don’t worry"));

    [Fact]
    public void Mantem_nome_proprio_com_apostrofo()
        => Assert.Equal(["Ask", "O'Brien"], Words("Ask O'Brien"));

    [Fact]
    public void Nao_engole_aspas_ao_redor_da_palavra()
        => Assert.Equal(["quoted", "text"], Words("'quoted' text"));

    [Fact]
    public void Separa_palavra_hifenizada_em_duas()
    {
        // Juntar seria mais correto em teoria, mas o dicionario en_US nao tem a maioria
        // dos compostos hifenizados -- e cada ausencia viraria sublinhado falso.
        Assert.Equal(["well", "known", "issue"], Words("well-known issue"));
    }

    [Fact]
    public void Registra_a_posicao_exata_de_cada_palavra()
    {
        const string text = "Please send it";
        var tokens = EnglishTokenizer.Tokenize(text);

        Assert.Equal(3, tokens.Count);

        foreach (var token in tokens)
        {
            // O deslocamento tem que ser exato: o Replace reconstroi o texto a partir dele.
            Assert.Equal(token.Text, text.Substring(token.Start, token.Length));
        }

        Assert.Equal(0, tokens[0].Start);
        Assert.Equal(7, tokens[1].Start);
        Assert.Equal(12, tokens[2].Start);
        Assert.Equal(14, tokens[2].End);
    }

    [Fact]
    public void Texto_vazio_nao_gera_palavra()
    {
        Assert.Empty(EnglishTokenizer.Tokenize(string.Empty));
        Assert.Empty(EnglishTokenizer.Tokenize("   \r\n  "));
        Assert.Empty(EnglishTokenizer.Tokenize("... --- !!!"));
    }
}
