using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WintleAutoClicker;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    [DllImport("user32.dll")]
    static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
    const int HOTKEY_ID = 1;
    const uint MOD_NONE = 0;

    readonly NumericUpDown interval = new() { Minimum = 1, Maximum = 60000, Value = 50, Width = 110 };
    readonly ComboBox button = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly Label status = new() { AutoSize = true, Text = "● Stopped" };
    CancellationTokenSource? cts;
    bool running;

    public MainForm()
    {
        Text = "Wintle-AutoClicker";
        ClientSize = new Size(360, 220);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        button.Items.AddRange(["Left Click", "Right Click"]);
        button.SelectedIndex = 0;

        var title = new Label { Text = "Wintle-AutoClicker", Font = new Font(Font.FontFamily, 16, FontStyle.Bold), AutoSize = true, Location = new Point(22, 18) };
        var intervalLabel = new Label { Text = "Click interval (ms)", AutoSize = true, Location = new Point(22, 68) };
        interval.Location = new Point(180, 64);
        var buttonLabel = new Label { Text = "Mouse button", AutoSize = true, Location = new Point(22, 105) };
        button.Location = new Point(180, 101);
        var hotkey = new Label { Text = "F6 = Start / Stop", AutoSize = true, Location = new Point(22, 145) };
        status.Location = new Point(22, 175);

        Controls.AddRange([title, intervalLabel, interval, buttonLabel, button, hotkey, status]);
        RegisterHotKey(Handle, HOTKEY_ID, MOD_NONE, (uint)Keys.F6);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_HOTKEY = 0x0312;
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID) Toggle();
        base.WndProc(ref m);
    }

    void Toggle()
    {
        if (running) Stop(); else Start();
    }

    void Start()
    {
        running = true;
        status.Text = "● Running — press F6 to stop";
        cts = new CancellationTokenSource();
        var token = cts.Token;
        int delay = (int)interval.Value;
        bool right = button.SelectedIndex == 1;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                mouse_event(right ? RIGHTDOWN : LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(right ? RIGHTUP : LEFTUP, 0, 0, 0, UIntPtr.Zero);
                try { await Task.Delay(delay, token); } catch (TaskCanceledException) { }
            }
        }, token);
    }

    void Stop()
    {
        running = false;
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
        status.Text = "● Stopped";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Stop();
        UnregisterHotKey(Handle, HOTKEY_ID);
        base.OnFormClosed(e);
    }
}
