using CorrectEnglish.Core.Input;
using Xunit;

namespace CorrectEnglish.Core.Tests;

public sealed class TripleTapDetectorTests
{
    private static TimeSpan Ms(double value) => TimeSpan.FromMilliseconds(value);

    private static TripleTapDetector Detector(double windowMs = 600)
        => new(tapsRequired: 3, window: Ms(windowMs));

    [Fact]
    public void Dispara_no_terceiro_toque_dentro_da_janela()
    {
        var detector = Detector();

        Assert.False(detector.RegisterTap(Ms(0)));
        Assert.False(detector.RegisterTap(Ms(150)));
        Assert.True(detector.RegisterTap(Ms(300)));
    }

    [Fact]
    public void Nao_dispara_quando_os_toques_estao_espalhados()
    {
        var detector = Detector();

        Assert.False(detector.RegisterTap(Ms(0)));
        Assert.False(detector.RegisterTap(Ms(700)));
        Assert.False(detector.RegisterTap(Ms(1400)));
    }

    [Fact]
    public void Dispara_no_limite_exato_da_janela()
    {
        var detector = Detector(windowMs: 600);

        Assert.False(detector.RegisterTap(Ms(0)));
        Assert.False(detector.RegisterTap(Ms(300)));
        Assert.True(detector.RegisterTap(Ms(600)));
    }

    [Fact]
    public void Toques_velhos_sao_descartados_mas_a_sequencia_recente_ainda_conta()
    {
        var detector = Detector();

        // Um Ctrl+C solto, e um bom tempo depois a sequencia de verdade.
        Assert.False(detector.RegisterTap(Ms(0)));
        Assert.False(detector.RegisterTap(Ms(5000)));
        Assert.False(detector.RegisterTap(Ms(5100)));
        Assert.True(detector.RegisterTap(Ms(5200)));
    }

    [Fact]
    public void Zera_depois_de_disparar()
    {
        var detector = Detector();

        detector.RegisterTap(Ms(0));
        detector.RegisterTap(Ms(100));
        Assert.True(detector.RegisterTap(Ms(200)));
        Assert.Equal(0, detector.PendingTaps);

        // Um quarto toque logo depois nao pode disparar de novo.
        Assert.False(detector.RegisterTap(Ms(250)));
    }

    [Fact]
    public void Copiar_normal_duas_vezes_nao_dispara()
    {
        var detector = Detector();

        Assert.False(detector.RegisterTap(Ms(0)));
        Assert.False(detector.RegisterTap(Ms(120)));
        Assert.Equal(2, detector.PendingTaps);
    }

    [Fact]
    public void Relogio_andando_para_tras_reinicia_em_vez_de_travar()
    {
        var detector = Detector();

        detector.RegisterTap(Ms(1000));
        detector.RegisterTap(Ms(1100));

        // Tempo retrocedeu: descarta o acumulado e recomeca deste toque.
        Assert.False(detector.RegisterTap(Ms(5)));
        Assert.Equal(1, detector.PendingTaps);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Exige_ao_menos_dois_toques(int tapsRequired)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TripleTapDetector(tapsRequired, Ms(600)));
    }

    [Fact]
    public void Exige_janela_positiva()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TripleTapDetector(3, TimeSpan.Zero));
    }
}
