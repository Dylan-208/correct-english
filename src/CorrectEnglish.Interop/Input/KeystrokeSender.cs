using System.Runtime.InteropServices;
using CorrectEnglish.Interop.Native;

namespace CorrectEnglish.Interop.Input;

/// <summary>Envia teclas sinteticas para a janela em primeiro plano.</summary>
public static class KeystrokeSender
{
    /// <summary>
    /// Assinatura carimbada em <c>dwExtraInfo</c> das teclas que nos geramos, para que o
    /// hook consiga distinguir o nosso Ctrl+V do que o usuario digitou. E redundante com
    /// <c>LLKHF_INJECTED</c>, de proposito: barato e evita um loop de auto-disparo.
    /// </summary>
    public static readonly IntPtr Signature = new(0x0043_0045); // 'C','E'

    /// <summary>Envia <c>Ctrl+V</c> para quem estiver em primeiro plano.</summary>
    /// <returns>False se o Windows recusar a injecao (UIPI, sessao bloqueada, app elevado).</returns>
    public static bool SendPaste()
    {
        var sequence = new List<INPUT>(8);

        // Solta modificadores que o usuario possa estar segurando. Sem isso, Shift preso
        // transforma o nosso Ctrl+V em Ctrl+Shift+V, que em muitos apps e "colar sem formato"
        // e em outros nao e colar nenhum.
        ReleaseIfHeld(sequence, NativeMethods.VK_SHIFT);
        ReleaseIfHeld(sequence, NativeMethods.VK_MENU);
        ReleaseIfHeld(sequence, NativeMethods.VK_LWIN);
        ReleaseIfHeld(sequence, NativeMethods.VK_RWIN);

        sequence.Add(Key(NativeMethods.VK_CONTROL, keyUp: false));
        sequence.Add(Key(NativeMethods.VK_V, keyUp: false));
        sequence.Add(Key(NativeMethods.VK_V, keyUp: true));
        sequence.Add(Key(NativeMethods.VK_CONTROL, keyUp: true));

        var inputs = sequence.ToArray();
        var sent = NativeMethods.SendInput(
            (uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        return sent == (uint)inputs.Length;
    }

    private static void ReleaseIfHeld(List<INPUT> sequence, int virtualKey)
    {
        if ((NativeMethods.GetKeyState(virtualKey) & 0x8000) != 0)
        {
            sequence.Add(Key(virtualKey, keyUp: true));
        }
    }

    private static INPUT Key(int virtualKey, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)virtualKey,
                wScan = 0,
                dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = Signature,
            },
        },
    };
}
