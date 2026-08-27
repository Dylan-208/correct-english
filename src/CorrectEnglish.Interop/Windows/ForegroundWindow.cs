using System.Text;
using CorrectEnglish.Interop.Native;

namespace CorrectEnglish.Interop.Windows;

/// <summary>Identifica a janela para onde o texto corrigido deve voltar.</summary>
public readonly record struct WindowSnapshot(IntPtr Handle, string Title, uint ProcessId)
{
    public static WindowSnapshot None => new(IntPtr.Zero, string.Empty, 0);

    public bool IsValid => Handle != IntPtr.Zero;

    /// <summary>True se a janela ainda existe agora.</summary>
    public bool StillExists => IsValid && NativeMethods.IsWindow(Handle);
}

/// <summary>
/// Captura e devolve o foco da janela onde o usuario estava.
/// <para>
/// A ordem importa: a captura tem que acontecer <b>antes</b> da nossa janela aparecer,
/// senao o alvo registrado passa a ser a nossa propria janela.
/// </para>
/// </summary>
public static class ForegroundWindow
{
    public static WindowSnapshot Capture()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return WindowSnapshot.None;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);

        var title = new StringBuilder(512);
        NativeMethods.GetWindowText(handle, title, title.Capacity);

        return new WindowSnapshot(handle, title.ToString(), processId);
    }

    /// <summary>
    /// Devolve o foco para a janela alvo.
    /// <para>
    /// O Windows normalmente bloqueia <c>SetForegroundWindow</c> vindo de um processo
    /// que nao esta em primeiro plano. Aqui funciona porque quem chama e a nossa janela,
    /// que <i>esta</i> em primeiro plano no momento em que o usuario aperta Replace.
    /// Se o app perder o foco antes disso, esta chamada falha -- e e por isso que ela
    /// retorna bool em vez de void.
    /// </para>
    /// </summary>
    public static bool TryRestore(IntPtr handle, int attempts = 4)
    {
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
        {
            return false;
        }

        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }

        for (var attempt = 0; attempt < Math.Max(1, attempts); attempt++)
        {
            NativeMethods.SetForegroundWindow(handle);

            if (NativeMethods.GetForegroundWindow() == handle)
            {
                return true;
            }

            Thread.Sleep(30);
        }

        return false;
    }
}
