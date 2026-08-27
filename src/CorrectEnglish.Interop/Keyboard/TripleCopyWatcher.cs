using System.Diagnostics;
using CorrectEnglish.Core.Input;
using CorrectEnglish.Interop.Native;

namespace CorrectEnglish.Interop.Keyboard;

/// <summary>
/// Junta o hook passivo ao <see cref="TripleTapDetector"/> e dispara
/// <see cref="TripleCopyDetected"/> quando o usuario aperta <c>Ctrl+C</c> tres vezes seguidas.
/// </summary>
public sealed class TripleCopyWatcher : IDisposable
{
    private readonly LowLevelKeyboardHook _hook = new();
    private readonly TripleTapDetector _detector;

    // Relogio monotonico. A hora do sistema nao serve: pode andar para tras.
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private bool _copyKeyHeld;
    private bool _disposed;

    public TripleCopyWatcher(TimeSpan? window = null)
    {
        _detector = new TripleTapDetector(tapsRequired: 3, window: window);
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;
    }

    /// <summary>Disparado na thread do hook -- a thread de UI, se instalado de la.</summary>
    public event EventHandler? TripleCopyDetected;

    public bool IsRunning => _hook.IsInstalled;

    public void Start() => _hook.Install();

    public void Stop()
    {
        _hook.Uninstall();
        _detector.Reset();
        _copyKeyHeld = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _hook.KeyDown -= OnKeyDown;
        _hook.KeyUp -= OnKeyUp;
        _hook.Dispose();
        _disposed = true;
    }

    private static bool IsModifier(int virtualKey) => virtualKey
        is NativeMethods.VK_CONTROL
        or NativeMethods.VK_SHIFT
        or NativeMethods.VK_MENU
        or NativeMethods.VK_LWIN
        or NativeMethods.VK_RWIN
        or 0xA0 // VK_LSHIFT
        or 0xA1 // VK_RSHIFT
        or 0xA2 // VK_LCONTROL
        or 0xA3 // VK_RCONTROL
        or 0xA4 // VK_LMENU
        or 0xA5; // VK_RMENU

    private void OnKeyDown(object? sender, KeyboardHookEventArgs e)
    {
        // Ignora o nosso proprio Ctrl+V do Replace, senao o app se auto-dispara.
        if (e.IsInjected)
        {
            return;
        }

        // Modificadores sozinhos nao interrompem a sequencia.
        if (IsModifier(e.VirtualKey))
        {
            return;
        }

        // Qualquer outra tecla cancela: Ctrl+C, "a", Ctrl+C, Ctrl+C nao e a sequencia.
        if (e.VirtualKey != NativeMethods.VK_C)
        {
            _detector.Reset();
            return;
        }

        // Auto-repeat do Windows manda varios KEYDOWN sem KEYUP no meio.
        if (_copyKeyHeld)
        {
            return;
        }

        _copyKeyHeld = true;

        // Precisa ser Ctrl+C limpo. Ctrl+Shift+C e devtools do navegador, nao copiar.
        var isCleanCopy =
            LowLevelKeyboardHook.IsKeyDown(NativeMethods.VK_CONTROL)
            && !LowLevelKeyboardHook.IsKeyDown(NativeMethods.VK_SHIFT)
            && !LowLevelKeyboardHook.IsKeyDown(NativeMethods.VK_MENU);

        if (!isCleanCopy)
        {
            _detector.Reset();
            return;
        }

        if (_detector.RegisterTap(_clock.Elapsed))
        {
            TripleCopyDetected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnKeyUp(object? sender, KeyboardHookEventArgs e)
    {
        if (!e.IsInjected && e.VirtualKey == NativeMethods.VK_C)
        {
            _copyKeyHeld = false;
        }
    }
}
