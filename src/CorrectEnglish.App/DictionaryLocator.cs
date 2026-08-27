// O SDK do WPF deixa System.IO fora dos implicit usings de proposito, para "Path" nao
// colidir com System.Windows.Shapes.Path. Aqui nao ha risco: este arquivo nao toca em XAML.
using System.IO;

namespace CorrectEnglish;

/// <summary>
/// Encontra os arquivos do dicionário Hunspell.
/// <para>
/// Procura em dois lugares porque há dois cenários: ao lado do executável (como o
/// instalador vai distribuir, na Fase 4) e em <c>assets/dictionaries</c> na raiz do
/// repositório (como fica durante o desenvolvimento, onde o executável está enterrado
/// em <c>bin/Debug/net8.0-windows</c>).
/// </para>
/// </summary>
internal static class DictionaryLocator
{
    private const string DictionaryFileName = "en_US.dic";
    private const string AffixFileName = "en_US.aff";

    internal readonly record struct Located(string DictionaryPath, string AffixPath);

    public static Located? TryLocate()
    {
        foreach (var directory in CandidateDirectories())
        {
            var dictionary = Path.Combine(directory, DictionaryFileName);
            var affix = Path.Combine(directory, AffixFileName);

            if (File.Exists(dictionary) && File.Exists(affix))
            {
                return new Located(dictionary, affix);
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var baseDirectory = AppContext.BaseDirectory;

        // Distribuído: ao lado do executável.
        yield return Path.Combine(baseDirectory, "dictionaries");

        // Desenvolvimento: sobe de bin/Debug/net8.0-windows até achar a raiz do repositório.
        var directory = new DirectoryInfo(baseDirectory);

        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            yield return Path.Combine(directory.FullName, "assets", "dictionaries");
            directory = directory.Parent;
        }
    }
}
