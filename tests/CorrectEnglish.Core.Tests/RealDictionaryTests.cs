using CorrectEnglish.Core.Correction;
using CorrectEnglish.Core.Spelling;
using Xunit;
using Xunit.Abstractions;

namespace CorrectEnglish.Core.Tests;

/// <summary>
/// Exercita o dicionário en_US de verdade, não um vocabulário de teste.
/// <para>
/// Os outros testes provam que a lógica está certa; estes provam que ela está certa
/// <i>com o dicionário que o usuário realmente vai usar</i> — que é onde moram as
/// surpresas. Se o dicionário não estiver baixado, cada teste sai sem asseverar nada,
/// para o suíte continuar verde num clone limpo.
/// </para>
/// </summary>
public sealed class RealDictionaryTests
{
    private readonly ITestOutputHelper _output;

    public RealDictionaryTests(ITestOutputHelper output) => _output = output;

    private static SpellingCorrectionProvider? TryCreateProvider()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (var depth = 0; depth < 8 && directory is not null; depth++)
        {
            var dictionary = Path.Combine(directory.FullName, "assets", "dictionaries", "en_US.dic");
            var affix = Path.Combine(directory.FullName, "assets", "dictionaries", "en_US.aff");

            if (File.Exists(dictionary) && File.Exists(affix))
            {
                return new SpellingCorrectionProvider(
                    HunspellSpellChecker.FromFiles(dictionary, affix, "en_US"));
            }

            directory = directory.Parent;
        }

        return null;
    }

    private async Task<CorrectionResult?> Correct(string text)
    {
        var provider = TryCreateProvider();

        if (provider is null)
        {
            _output.WriteLine("Dicionário en_US ausente; rode scripts/get-dictionaries.ps1.");
            return null;
        }

        var result = await provider.CorrectAsync(text, Tone.Neutral);

        foreach (var correction in result.Corrections)
        {
            _output.WriteLine($"{correction.From} -> {correction.To}");
        }

        return result;
    }

    [Fact]
    public async Task Frase_correta_nao_gera_nenhuma_correcao()
    {
        var result = await Correct("Please confirm that everything is correct with the numbers.");
        if (result is null)
        {
            return;
        }

        Assert.Empty(result.Corrections);
        Assert.True(result.IsUnchanged);
    }

    [Fact]
    public async Task Pega_erros_classicos_de_quem_escreve_ingles_como_segunda_lingua()
    {
        var result = await Correct("I recieve the reprot about the meetting");
        if (result is null)
        {
            return;
        }

        var corrected = result.CorrectedText;

        Assert.Contains("receive", corrected, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report", corrected, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meeting", corrected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O teste que mais importa. Um corretor que sublinha código, URL e nome de usuário é
    /// pior que nenhum corretor: o usuário aprende a ignorar o sublinhado e o app perde
    /// a razão de existir.
    /// </summary>
    [Fact]
    public async Task Nao_produz_falso_positivo_em_texto_tecnico_real()
    {
        const string text =
            "Please check https://github.com/Dylan-208/correct-english and call the "
            + "getUserById method. Send the result to dylansilva208@gmail.com or ping "
            + "@sarah about it. I don't think the API needs user_id for that.";

        var result = await Correct(text);
        if (result is null)
        {
            return;
        }

        Assert.Empty(result.Corrections);
        Assert.Equal(text, result.CorrectedText);
    }

    [Fact]
    public async Task Aceita_contracao_com_os_dois_tipos_de_apostrofo()
    {
        var straight = await Correct("I don't think it's ready and I won't wait");
        if (straight is null)
        {
            return;
        }

        Assert.Empty(straight.Corrections);

        // A mesma frase como o Word e o WhatsApp gravam, com apóstrofo tipográfico.
        var curly = await Correct("I don’t think it’s ready and I won’t wait");
        Assert.Empty(curly!.Corrections);
    }

    [Fact]
    public async Task Preserva_maiuscula_no_inicio_da_frase()
    {
        var result = await Correct("Recieve the file please");
        if (result is null)
        {
            return;
        }

        Assert.StartsWith("Receive", result.CorrectedText, StringComparison.Ordinal);
    }
}
