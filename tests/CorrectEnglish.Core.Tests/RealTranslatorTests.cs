using CorrectEnglish.Core.Translation;
using Xunit;
using Xunit.Abstractions;

namespace CorrectEnglish.Core.Tests;

/// <summary>
/// Exercita o servidor LibreTranslate de verdade em <c>localhost:5000</c>.
/// <para>
/// Os testes com handler de mentira provam a forma da requisição; estes provam que o
/// servidor real responde e que os bytes chegam decodificados. Se o contêiner não estiver
/// de pé, cada teste sai sem asseverar nada — o suíte não pode depender de Docker.
/// </para>
/// </summary>
public sealed class RealTranslatorTests
{
    private readonly ITestOutputHelper _output;

    public RealTranslatorTests(ITestOutputHelper output) => _output = output;

    private static LibreTranslateTranslator Create()
        => new(new Uri("http://localhost:5000"));

    private async Task<LibreTranslateTranslator?> TryCreateAvailable()
    {
        var translator = Create();

        if (await translator.IsAvailableAsync())
        {
            return translator;
        }

        _output.WriteLine("LibreTranslate fora do ar; rode \"docker compose up -d\".");
        translator.Dispose();
        return null;
    }

    [Fact]
    public async Task Servidor_real_traduz_do_ingles_para_o_portugues()
    {
        using var translator = await TryCreateAvailable();
        if (translator is null)
        {
            return;
        }

        var result = await translator.TranslateAsync("I sent you the report yesterday.");

        Assert.NotNull(result);
        _output.WriteLine(result);

        // Nao asseveramos a traducao exata -- o modelo pode mudar de versao. Asseveramos
        // que traduziu: o resultado nao pode ser o ingles de volta.
        Assert.DoesNotContain("report", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("relat", result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O teste que motivou este arquivo. O console do PowerShell mostrou "relatÃ³rio" ao
    /// chamar a API, o que parecia corrupção de acento. Era artefato do console, mas
    /// "parecia" não é garantia: se o cliente .NET decodificasse errado, o Replace
    /// gravaria mojibake no documento do usuário.
    /// </summary>
    [Fact]
    public async Task Acentos_chegam_intactos_do_servidor_real()
    {
        using var translator = await TryCreateAvailable();
        if (translator is null)
        {
            return;
        }

        var result = await translator.TranslateAsync("The report about the meeting is ready.");

        Assert.NotNull(result);
        _output.WriteLine(result);

        // Sinais de UTF-8 lido como Latin-1.
        Assert.DoesNotContain("Ã", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Â", result, StringComparison.Ordinal);
        Assert.DoesNotContain("�", result, StringComparison.Ordinal); // caractere de substituição

        // E o inverso: tem que haver acento de verdade, senão o teste passaria com
        // um texto sem acento nenhum e não provaria nada.
        Assert.Contains(
            result!.ToCharArray(),
            c => "áàâãéêíóôõúçÁÀÂÃÉÊÍÓÔÕÚÇ".Contains(c, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Servidor_disponivel_e_reportado_como_disponivel()
    {
        using var translator = Create();
        var available = await translator.IsAvailableAsync();

        _output.WriteLine($"LibreTranslate disponível: {available}");

        // Sem assercao: este teste existe para registrar o estado no log da execucao,
        // e nao para falhar quando o Docker esta desligado.
    }

    [Fact]
    public async Task Porta_errada_e_reportada_como_indisponivel_rapidamente()
    {
        // Garante que a degradacao nao depende de esperar o timeout de 25 s do translate.
        using var translator = new LibreTranslateTranslator(new Uri("http://localhost:5999"));

        var started = DateTime.UtcNow;
        var available = await translator.IsAvailableAsync();
        var elapsed = DateTime.UtcNow - started;

        Assert.False(available);
        Assert.True(elapsed < TimeSpan.FromSeconds(6), $"demorou {elapsed.TotalSeconds:0.0}s");
    }
}
