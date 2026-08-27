using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace CorrectEnglish;

/// <summary>
/// Icone na bandeja do sistema. Usa o <c>NotifyIcon</c> do WinForms, que vem no SDK --
/// o WPF nao tem equivalente nativo, e nao vale uma dependencia externa por isso.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private bool _disposed;

    public TrayIcon()
    {
        _icon = CreateIcon();

        var menu = new WinForms.ContextMenuStrip();

        menu.Items.Add(new WinForms.ToolStripMenuItem("Selecione um texto e aperte Ctrl+C 3x")
        {
            Enabled = false,
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var about = new WinForms.ToolStripMenuItem("Sobre");
        about.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(about);

        var exit = new WinForms.ToolStripMenuItem("Sair");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exit);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = "Correct English", // limite de 63 caracteres
            Visible = true,
            ContextMenuStrip = menu,
        };
    }

    public event EventHandler? ExitRequested;

    public event EventHandler? AboutRequested;

    public void Notify(string title, string message)
        => _notifyIcon.ShowBalloonTip(4000, title, message, WinForms.ToolTipIcon.None);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Desenha o icone em tempo de execucao: circulo vermelho com um "check".
    /// Evita carregar um .ico na Fase 1 -- um icone de verdade entra na Fase 4,
    /// junto com o instalador.
    /// </summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var fill = new SolidBrush(Color.FromArgb(0xC0, 0x34, 0x2F));
            graphics.FillEllipse(fill, 2, 2, 28, 28);

            using var pen = new Pen(Color.White, 3.4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            graphics.DrawLines(pen,
            [
                new PointF(9.5f, 16.5f),
                new PointF(14f, 21.5f),
                new PointF(22.5f, 10.5f),
            ]);
        }

        // GetHicon aloca um HICON nao gerenciado. Clonamos para um Icon gerenciado
        // e devolvemos o handle, senao vaza a cada execucao.
        var handle = bitmap.GetHicon();
        try
        {
            using var unmanaged = Icon.FromHandle(handle);
            return (Icon)unmanaged.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
