using System.ComponentModel;
using System.Runtime.InteropServices;
using CorrectEnglish.Interop.Native;

namespace CorrectEnglish.Interop.Keyboard;

public sealed class KeyboardHookEventArgs : EventArgs
{
    internal KeyboardHookEventArgs(int virtualKey, bool isInjected, IntPtr extraInfo)
    {
        VirtualKey = virtualKey;
        IsInjected = isInjected;
        ExtraInfo = extraInfo;
    }

    public int VirtualKey { get; }

    /// <summary>True quando a tecla foi gerada por software -- inclusive pelo nosso proprio Ctrl+V.</summary>
    public bool IsInjected { get; }

    public IntPtr ExtraInfo { get; }
}

/// <summary>
/// Hook global de teclado (<c>WH_KEYBOARD_LL</c>), estritamente <b>passivo</b>:
/// observa e sempre repassa a tecla adiante. Nunca engole nada.
/// <para>
/// Por que passivo: registrar <c>Ctrl+C</c> como atalho global (via <c>RegisterHotKey</c>)
/// roubaria o copiar do sistema inteiro. O hook permite contar os toques sem interferir.
/// </para>
/// <para>
/// <b>Instale a partir de uma thread com bomba de mensagens</b> -- a thread de UI do WPF serve.
/// E mantenha o callback rapido: o Windows desinstala hooks que estouram
/// <c>LowLevelHooksTimeout</c> (300 ms por padrao).
/// </para>
/// <para>
/// Este tipo e a razao pela qual o app precisa de lista de permissao por aplicativo:
/// e literalmente a mesma API de um keylogger. Ver a secao Privacidade do README.
/// </para>
/// </summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    // O delegate precisa ficar vivo enquanto o hook existir. Se virar lixo,
    // o Windows chama memoria liberada e o processo morre sem explicacao.
    private readonly NativeMethods.LowLevelKeyboardProc _callback;

    private IntPtr _handle;
    private bool _disposed;

    public LowLevelKeyboardHook() => _callback = HookCallback;

    public event EventHandler<KeyboardHookEventArgs>? KeyDown;

    public event EventHandler<KeyboardHookEventArgs>? KeyUp;

    public bool IsInstalled => _handle != IntPtr.Zero;

    /// <summary>Diz se a tecla esta fisicamente pressionada agora.</summary>
    public static bool IsKeyDown(int virtualKey)
        => (NativeMethods.GetKeyState(virtualKey) & 0x8000) != 0;

    /// <exception cref="Win32Exception">Se o Windows recusar a instalacao do hook.</exception>
    public void Install()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsInstalled)
        {
            return;
        }

        var module = NativeMethods.GetModuleHandle(null);
        _handle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _callback, module, 0);

        if (_handle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Nao foi possivel instalar o hook de teclado.");
        }
    }

    public void Uninstall()
    {
        if (!IsInstalled)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_handle);
        _handle = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Uninstall();
        _disposed = true;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            try
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var args = new KeyboardHookEventArgs(
                    (int)data.vkCode,
                    (data.flags & NativeMethods.LLKHF_INJECTED) != 0,
                    data.dwExtraInfo);

                switch ((int)wParam)
                {
                    case NativeMethods.WM_KEYDOWN:
                    case NativeMethods.WM_SYSKEYDOWN:
                        KeyDown?.Invoke(this, args);
                        break;
                    case NativeMethods.WM_KEYUP:
                    case NativeMethods.WM_SYSKEYUP:
                        KeyUp?.Invoke(this, args);
                        break;
                }
            }
            catch
            {
                // Uma excecao escapando daqui atravessa a fronteira nativa e derruba o
                // processo. Engolir e a unica opcao correta dentro de um hook.
            }
        }

        // Sempre repassa. Este hook nunca bloqueia tecla nenhuma.
        return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
    }
}
