using System.Runtime.InteropServices;
using CorrectEnglish.Interop.Native;

namespace CorrectEnglish.Interop.Windows;

/// <summary>Ponto em pixels fisicos de tela -- <b>nao</b> em DIPs do WPF.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>
/// Descobre onde esta o cursor de texto, para a janela aparecer perto de onde o usuario
/// esta escrevendo em vez de no meio da tela.
/// <para>
/// Este e o mesmo <c>GetGUIThreadInfo</c> que a Fase 3 vai usar para posicionar o aviso
/// em tempo real (ver ADR 0003) -- de proposito: se ele se provar confiavel aqui, a
/// Fase 3 herda o codigo testado.
/// </para>
/// </summary>
public static class CaretLocator
{
    /// <summary>
    /// Posicao do caret na tela, ou <c>null</c> quando o app nao publica um caret --
    /// o caso de Electron, editores em canvas e a maior parte dos apps modernos.
    /// </summary>
    public static ScreenPoint? TryGetCaretPosition(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out _);
        if (threadId == 0)
        {
            return null;
        }

        var info = new GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>(),
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info))
        {
            return null;
        }

        if (info.hwndCaret == IntPtr.Zero || info.rcCaret.IsEmpty)
        {
            return null;
        }

        // rcCaret e relativo ao cliente de hwndCaret. Ancora no canto inferior esquerdo,
        // para a janela abrir logo abaixo da linha que esta sendo digitada.
        var point = new POINT { X = info.rcCaret.Left, Y = info.rcCaret.Bottom };

        return NativeMethods.ClientToScreen(info.hwndCaret, ref point)
            ? new ScreenPoint(point.X, point.Y)
            : null;
    }

    public static ScreenPoint GetCursorPosition()
        => NativeMethods.GetCursorPos(out var point)
            ? new ScreenPoint(point.X, point.Y)
            : new ScreenPoint(0, 0);

    /// <summary>Caret quando disponivel, ponteiro do mouse como reserva.</summary>
    public static ScreenPoint GetAnchorPoint(IntPtr windowHandle)
        => TryGetCaretPosition(windowHandle) ?? GetCursorPosition();
}
