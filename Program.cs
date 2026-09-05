using System;
using System.Diagnostics;
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
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const uint INPUT_MOUSE = 0;
    const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
    const int HOTKEY_ID = 1;

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    readonly NumericUpDown interval = new() { Minimum = 1, Maximum = 60000, Value = 50, Width = 120 };
    readonly ComboBox button = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly NumericUpDown limit = new() { Minimum = 0, Maximum = 1000000, Value = 0, Width = 120 };
    readonly CheckBox randomize = new() { Text = "Randomize interval", AutoSize = true };
    readonly NumericUpDown randomAmount = new() { Minimum = 0, Maximum = 1000, Value = 10, Width = 120, Enabled = false };
    readonly Button start = new();
    readonly Label status = new();
    readonly Label cps = new();
    readonly Label count = new();
    readonly Timer stats = new() { Interval = 250 };
    readonly Random rng = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    CancellationTokenSource? cts;
    long totalClicks, windowClicks, windowStart;
    bool running;

    public MainForm()
    {
        Text = "Wintle-AutoClicker";
        ClientSize = new Size(430, 430);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 18, 22);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        button.Items.AddRange(["Left Click", "Right Click"]);
        button.SelectedIndex = 0;
        randomize.CheckedChanged += (_, _) => randomAmount.Enabled = randomize.Checked;

        var title = new Label { Text = "WINTLE", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(25, 20) };
        var subtitle = new Label { Text = "AUTOCLICKER", Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Location = new Point(27, 56), ForeColor = Color.Silver };
        var card = new Panel { Location = new Point(20, 90), Size = new Size(390, 260), BackColor = Color.FromArgb(27, 27, 33) };

        AddRow(card, "Click interval (ms)", interval, 22, 24);
        AddRow(card, "Mouse button", button, 22, 72);
        AddRow(card, "Click limit (0 = ∞)", limit, 22, 120);
        card.Controls.Add(randomize); randomize.Location = new Point(22, 170);
        card.Controls.Add(randomAmount); randomAmount.Location = new Point(220, 166);
        card.Controls.Add(new Label { Text = "± ms", AutoSize = true, Location = new Point(345, 171), ForeColor = Color.Silver });

        start.Text = "START  •  F6"; start.Location = new Point(20, 365); start.Size = new Size(190, 48);
        start.FlatStyle = FlatStyle.Flat; start.FlatAppearance.BorderSize = 0; start.BackColor = Color.FromArgb(65, 150, 255); start.ForeColor = Color.White;
        start.Font = new Font("Segoe UI", 10, FontStyle.Bold); start.Click += (_, _) => Toggle();
        status.Text = "●  Stopped"; status.AutoSize = true; status.Location = new Point(230, 365); status.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        cps.Text = "CPS   0.0"; cps.AutoSize = true; cps.Location = new Point(230, 391); cps.ForeColor = Color.Silver;
        count.Text = "Clicks   0"; count.AutoSize = true; count.Location = new Point(315, 391); count.ForeColor = Color.Silver;

        Controls.AddRange([title, subtitle, card, start, status, cps, count]);
        windowStart = clock.ElapsedMilliseconds;
        stats.Tick += (_, _) => UpdateStats(); stats.Start();
        RegisterHotKey(Handle, HOTKEY_ID, 0, (uint)Keys.F6);
    }

    static void AddRow(Control parent, string text, Control control, int x, int y)
    {
        parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y + 4), Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        control.Location = new Point(220, y); parent.Controls.Add(control);
        if (control is NumericUpDown n) { n.BackColor = Color.FromArgb(38, 38, 46); n.ForeColor = Color.White; }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == HOTKEY_ID) Toggle();
        base.WndProc(ref m);
    }

    void Toggle() { if (running) Stop(); else Start(); }

    void Start()
    {
        if (running) return;
        running = true; totalClicks = 0; windowClicks = 0; windowStart = clock.ElapsedMilliseconds;
        cts = new CancellationTokenSource();
        start.Text = "STOP  •  F6"; start.BackColor = Color.FromArgb(220, 75, 75); status.Text = "●  Running"; status.ForeColor = Color.LightGreen;
        var token = cts.Token; bool right = button.SelectedIndex == 1; int baseDelay = (int)interval.Value; int maxClicks = (int)limit.Value;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && (maxClicks == 0 || Interlocked.Read(ref totalClicks) < maxClicks))
            {
                SendMouseClick(right);
                Interlocked.Increment(ref totalClicks); Interlocked.Increment(ref windowClicks);
                int delay = baseDelay;
                if (randomize.Checked) delay = Math.Max(1, baseDelay + rng.Next(-(int)randomAmount.Value, (int)randomAmount.Value + 1));
                try { await Task.Delay(delay, token); } catch (TaskCanceledException) { break; }
            }
            if (!token.IsCancellationRequested && IsHandleCreated) BeginInvoke(Stop);
        }, token);
    }

    static void SendMouseClick(bool right)
    {
        uint down = right ? RIGHTDOWN : LEFTDOWN, up = right ? RIGHTUP : LEFTUP;
        var input = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = down } }, new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = up } } };
        SendInput(2, input, Marshal.SizeOf<INPUT>());
    }

    void Stop()
    {
        running = false; cts?.Cancel(); cts?.Dispose(); cts = null;
        start.Text = "START  •  F6"; start.BackColor = Color.FromArgb(65, 150, 255); status.Text = "●  Stopped"; status.ForeColor = Color.White;
    }

    void UpdateStats()
    {
        long now = clock.ElapsedMilliseconds, elapsed = now - windowStart;
        if (elapsed >= 1000)
        {
            double value = Interlocked.Exchange(ref windowClicks, 0) * 1000.0 / elapsed;
            cps.Text = $"CPS   {value:0.0}"; windowStart = now;
        }
        count.Text = $"Clicks   {Interlocked.Read(ref totalClicks):N0}";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Stop(); stats.Stop(); UnregisterHotKey(Handle, HOTKEY_ID); base.OnFormClosed(e);
    }
}
