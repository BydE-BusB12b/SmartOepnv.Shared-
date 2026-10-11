using System.Text;

namespace SmartOepnv.IbisWagenbusUtility;

public sealed class MainForm : Form
{
    private readonly IbisSerialLink _link = new();

    private readonly ComboBox _ports = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly Button _refreshPorts = new() { Text = "Aktualisieren", AutoSize = true };
    private readonly Button _open = new() { Text = "Öffnen", AutoSize = true };
    private readonly Button _close = new() { Text = "Schließen", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { AutoSize = true, Text = "Kein Port", Padding = new Padding(8, 8, 0, 0) };

    private readonly TextBox _line = new() { Text = "000", Width = 56 };
    private readonly TextBox _special = new() { Text = "E03", Width = 56 };
    private readonly TextBox _zielNr = new() { Text = "003", Width = 56 };
    private readonly NumericUpDown _interval = new() { Minimum = 1, Maximum = 8, Value = 3, Width = 48 };

    private readonly TextBox[] _z1 = MakeGoalBoxes();
    private readonly TextBox[] _z2 = MakeGoalBoxes();
    private readonly TextBox[] _z3 = MakeGoalBoxes();
    private readonly TextBox[] _z4 = MakeGoalBoxes();
    private readonly TextBox _side1 = new() { Width = 200 };
    private readonly TextBox _side2 = new() { Width = 200 };

    private readonly TextBox _ds009 = new() { Width = 360 };
    private readonly TextBox _gorbaNext = new() { Width = 280 };
    private readonly TextBox _gorbaDest = new() { Width = 280 };
    private readonly NumericUpDown _gorbaIdx = new() { Minimum = 0, Maximum = 9, Value = 0, Width = 48 };
    private readonly TextBox _gorbaStop = new() { Width = 240 };

    private readonly TextBox _rawAscii = new() { Width = 480 };
    private readonly TextBox _rawHex = new() { Width = 480, Height = 56, Multiline = true };
    private readonly CheckBox _hexAppendParity = new() { Text = "CR+Parity anhängen falls fehlt", AutoSize = true, Checked = false };

    private readonly TextBox _log = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        Font = new Font("Consolas", 9f),
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WordWrap = false
    };

    private readonly StringBuilder _rxAccumulate = new();
    private DateTime _lastRxAt = DateTime.MinValue;

    public MainForm()
    {
        Text = "IBIS Wagenbus Utility";
        Width = 980;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 640);

        _mode.Items.AddRange(
        [
            "Standard IBIS (1200 7E2 + XOR)",
            "Gorba TFT (1200 7E1, nur CR)"
        ]);
        _mode.SelectedIndex = 0;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        var conn = Row(
            L("COM"), _ports, _refreshPorts,
            L("Modus"), _mode,
            _open, _close, _status);
        root.Controls.Add(conn, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildBasicsTab());
        tabs.TabPages.Add(BuildDs021tTab());
        tabs.TabPages.Add(BuildDs021NeuFmaTab());
        tabs.TabPages.Add(BuildInteriorGorbaTab());
        tabs.TabPages.Add(BuildRawTab());
        root.Controls.Add(tabs, 0, 1);

        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        var logHeader = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        logHeader.Controls.Add(Section("TX / RX Log"));
        logHeader.Controls.Add(Btn("Log leeren", (_, _) =>
        {
            _log.Clear();
            _rxAccumulate.Clear();
        }));
        logHost.Controls.Add(_log);
        logHost.Controls.Add(logHeader);
        root.Controls.Add(logHost, 0, 2);

        Controls.Add(root);

        _refreshPorts.Click += (_, _) => RefreshPorts();
        _open.Click += (_, _) => OpenPort();
        _close.Click += (_, _) => ClosePort();
        FormClosing += (_, _) => _link.Dispose();

        _link.BytesReceived += OnRx;
        _link.StatusChanged += s => BeginInvoke(() => AppendLog("— " + s));
        _link.ErrorOccurred += ex => BeginInvoke(() => AppendLog("RX-Fehler: " + ex.Message));

        RefreshPorts();
    }

    private TabPage BuildBasicsTab()
    {
        var page = new TabPage("DS001 / DS003 / zA4 / Krefeld");
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8)
        };
        p.Controls.Add(Hint("Linie / Sonderzeichen / Zielnummer / Clear – Standard-XOR wie Ibis Utility."));
        p.Controls.Add(Row(
            L("Linie"), _line, Btn("l###", (_, _) => SafeSend(() => IbisFormat.Line(_line.Text))),
            L("Sonderz."), _special, Btn("lExx", (_, _) => SafeSend(() => IbisFormat.Special(_special.Text))),
            L("Zielnr."), _zielNr, Btn("z###", (_, _) => SafeSend(() => IbisFormat.DestinationNumber(_zielNr.Text))),
            Btn("Clear zA0", (_, _) => SafeSend(IbisFormat.ClearDestination))));
        p.Controls.Add(Section("Einfacher Text (Ziel 1)"));
        p.Controls.Add(Row(L("Zeile 1"), _z1[0], L("Zeile 2"), _z1[1]));
        p.Controls.Add(Row(
            Btn("zA4 (2×16)", (_, _) => SafeSend(() => IbisFormat.TwoLineZa4(_z1[0].Text, _z1[1].Text))),
            Btn("Krefeld zA5", (_, _) => SafeSend(() => IbisFormat.Krefeld(_z1[0].Text, _z1[1].Text, _side1.Text, _side2.Text))),
            Btn("Krefeld leer", (_, _) => SafeSend(() => IbisFormat.KrefeldEmpty()))));
        p.Controls.Add(Row(L("Seite 1"), _side1, L("Seite 2"), _side2));
        page.Controls.Add(p);
        return page;
    }

    private TabPage BuildDs021tTab()
    {
        var page = new TabPage("DS021T Wechseltext");
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8)
        };
        p.Controls.Add(Hint("Bis zu 4 Ziele (Front Zeile1/2). Intervall für aA… Header."));
        p.Controls.Add(Row(L("Intervall (s)"), _interval));
        p.Controls.Add(GoalRow("Ziel 1", _z1));
        p.Controls.Add(GoalRow("Ziel 2", _z2));
        p.Controls.Add(GoalRow("Ziel 3", _z3));
        p.Controls.Add(GoalRow("Ziel 4", _z4));
        p.Controls.Add(Row(
            Btn("Front aA1… senden", (_, _) => SafeSend(() =>
                IbisFormat.Ds021tFront(CollectGoals(), (int)_interval.Value))),
            Btn("Seite aA2… senden", (_, _) => SafeSend(() =>
                IbisFormat.Ds021tSide(CollectGoals(), (int)_interval.Value))),
            Btn("Front + Seite", (_, _) => SafeSendMany(() =>
            {
                var goals = CollectGoals();
                var iv = (int)_interval.Value;
                return
                [
                    IbisFormat.Ds021tFront(goals, iv),
                    IbisFormat.Ds021tSide(goals, iv)
                ];
            }))));
        page.Controls.Add(p);
        return page;
    }

    private TabPage BuildDs021NeuFmaTab()
    {
        var page = new TabPage("DS021neu / FMA-S1");
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8)
        };
        p.Controls.Add(Hint("Nutzt Ziel 1 (+ optional Seite). FMA: Linie/Sonderz. aus erstem Reiter für .Y-Feld."));
        p.Controls.Add(Row(
            Btn("DS021neu Front", (_, _) => SafeSend(() => IbisFormat.Ds021NeuFront(_z1[0].Text, _z1[1].Text))),
            Btn("DS021neu Seite", (_, _) => SafeSend(() =>
                IbisFormat.Ds021NeuSide(
                    string.IsNullOrWhiteSpace(_side1.Text) ? _z1[0].Text : _side1.Text,
                    string.IsNullOrWhiteSpace(_side2.Text) ? _z1[1].Text : _side2.Text))),
            Btn("DS021neu Front+Seite", (_, _) => SafeSendMany(() =>
            {
                var (f, s) = IbisFormat.Ds021NeuBoth(_z1[0].Text, _z1[1].Text, _side1.Text, _side2.Text);
                return [f, s];
            }))));
        p.Controls.Add(Row(
            Btn("FMA-S1 Front+Seite", (_, _) => SafeSendMany(() =>
            {
                var (f, s) = IbisFormat.FmaS1(CollectGoals(), _line.Text, _special.Text);
                return [f, s];
            }))));
        page.Controls.Add(p);
        return page;
    }

    private TabPage BuildInteriorGorbaTab()
    {
        var page = new TabPage("DS009 / Gorba TFT");
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8)
        };
        p.Controls.Add(Section("DS009 Innenanzeige (Standard 7E2 + Parität)"));
        p.Controls.Add(Row(L("Halttext (max 20)"), _ds009,
            Btn("v… senden", (_, _) => SafeSend(() => IbisFormat.Ds009StopText(_ds009.Text)))));

        p.Controls.Add(Section("Gorba TFT (Modus auf 7E1 stellen!)"));
        p.Controls.Add(Hint("DS005 = nächste Hst., DS006 = Perlschnur Index 0–9, DS003 Zieltext ohne XOR."));
        p.Controls.Add(Row(L("Nächste Hst."), _gorbaNext,
            Btn("DS005 a…", (_, _) => SafeSend(() => IbisFormat.GorbaDs005(_gorbaNext.Text)))));
        p.Controls.Add(Row(L("Index"), _gorbaIdx, L("Text"), _gorbaStop,
            Btn("DS006 b…", (_, _) => SafeSend(() => IbisFormat.GorbaDs006((int)_gorbaIdx.Value, _gorbaStop.Text)))));
        p.Controls.Add(Row(L("Ziel"), _gorbaDest,
            Btn("DS003 z…", (_, _) => SafeSend(() => IbisFormat.GorbaDs003Destination(_gorbaDest.Text))),
            Btn("DS001 Linie (Gorba)", (_, _) => SafeSend(() => IbisFormat.GorbaDs001Line(_line.Text)))));
        p.Controls.Add(Row(Btn("Gorba-Test: DS005 + 3× DS006", (_, _) => SafeSendMany(() =>
        [
            IbisFormat.GorbaDs005(string.IsNullOrWhiteSpace(_gorbaNext.Text) ? "Test Halt" : _gorbaNext.Text),
            IbisFormat.GorbaDs006(0, string.IsNullOrWhiteSpace(_gorbaStop.Text) ? "Halt 0" : _gorbaStop.Text),
            IbisFormat.GorbaDs006(1, "Halt 1"),
            IbisFormat.GorbaDs006(2, "Halt 2")
        ], delayMs: 80))));
        page.Controls.Add(p);
        return page;
    }

    private TabPage BuildRawTab()
    {
        var page = new TabPage("Raw");
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8)
        };
        p.Controls.Add(Row(L("ASCII (ohne CR)"), _rawAscii,
            Btn("Senden (+CR+Parity)", (_, _) => SafeSend(() => IbisFormat.FromAsciiPayload(_rawAscii.Text)))));
        p.Controls.Add(Row(L("Hex"), _rawHex));
        p.Controls.Add(Row(_hexAppendParity,
            Btn("Hex senden", (_, _) => SafeSend(() => IbisFormat.FromHex(_rawHex.Text, _hexAppendParity.Checked))),
            Btn("Beispiel l000", (_, _) =>
            {
                _rawHex.Text = "6C,30,30,30,0D,38";
                _hexAppendParity.Checked = false;
            })));
        page.Controls.Add(p);
        return page;
    }

    private List<(string, string)> CollectGoals()
    {
        var goals = new List<(string, string)>();
        void Add(TextBox[] g)
        {
            var a = g[0].Text.Trim();
            var b = g[1].Text.Trim();
            if (a.Length > 0 || b.Length > 0)
            {
                goals.Add((a, b));
            }
        }

        Add(_z1);
        Add(_z2);
        Add(_z3);
        Add(_z4);
        return goals;
    }

    private void RefreshPorts()
    {
        var selected = _ports.SelectedItem as string;
        _ports.Items.Clear();
        foreach (var name in IbisSerialLink.GetPortNames())
        {
            _ports.Items.Add(name);
        }

        if (_ports.Items.Count == 0)
        {
            _status.Text = "Kein COM-Port";
            return;
        }

        if (selected != null && _ports.Items.Contains(selected))
        {
            _ports.SelectedItem = selected;
        }
        else
        {
            _ports.SelectedIndex = 0;
        }
    }

    private void OpenPort()
    {
        if (_ports.SelectedItem is not string name)
        {
            MessageBox.Show(this, "Bitte COM-Port wählen.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var mode = _mode.SelectedIndex == 1 ? IbisSerialMode.Gorba7E1 : IbisSerialMode.Standard7E2;
            _link.Open(name, mode);
            _open.Enabled = false;
            _close.Enabled = true;
            _ports.Enabled = false;
            _mode.Enabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Port öffnen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClosePort()
    {
        _link.Close();
        _open.Enabled = true;
        _close.Enabled = false;
        _ports.Enabled = true;
        _mode.Enabled = true;
    }

    private void SafeSend(Func<byte[]> build)
    {
        try
        {
            var bytes = build();
            if (_link.Mode == IbisSerialMode.Gorba7E1 && LooksLikeXorTelegram(bytes))
            {
                AppendLog("Hinweis: Standard-Telegramm mit XOR im Gorba-7E1-Modus – ggf. Modus wechseln.");
            }

            _link.Send(bytes);
            AppendLog($"out-> {IbisFormat.ToAsciiLog(bytes)}");
            AppendLog($"out-> <{IbisFormat.ToHexCsv(bytes)}>");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Senden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            AppendLog("TX-Fehler: " + ex.Message);
        }
    }

    private void SafeSendMany(Func<byte[][]> build, int delayMs = 50)
    {
        try
        {
            var list = build();
            foreach (var bytes in list)
            {
                _link.Send(bytes);
                AppendLog($"out-> {IbisFormat.ToAsciiLog(bytes)}");
                AppendLog($"out-> <{IbisFormat.ToHexCsv(bytes)}>");
                if (delayMs > 0)
                {
                    Thread.Sleep(delayMs);
                    Application.DoEvents();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Senden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            AppendLog("TX-Fehler: " + ex.Message);
        }
    }

    private static bool LooksLikeXorTelegram(byte[] bytes) =>
        bytes.Length >= 2 && bytes[^2] == 0x0D;

    private void OnRx(byte[] chunk)
    {
        BeginInvoke(() =>
        {
            var now = DateTime.Now;
            if ((now - _lastRxAt).TotalMilliseconds > 80 && _rxAccumulate.Length > 0)
            {
                FlushRxLine();
            }

            _lastRxAt = now;
            foreach (var b in chunk)
            {
                _rxAccumulate.Append((char)b);
                if (b == 0x0D)
                {
                    FlushRxLine();
                }
            }

            AppendLog($"in-  <{IbisFormat.ToHexCsv(chunk)}>");
        });
    }

    private void FlushRxLine()
    {
        if (_rxAccumulate.Length == 0)
        {
            return;
        }

        var raw = Encoding.Latin1.GetBytes(_rxAccumulate.ToString());
        AppendLog($"in-> {IbisFormat.ToAsciiLog(raw)}");
        _rxAccumulate.Clear();
    }

    private void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss.fffffff");
        _log.AppendText($"{stamp}  {line}{Environment.NewLine}");
    }

    private static TextBox[] MakeGoalBoxes() =>
    [
        new TextBox { Width = 180 },
        new TextBox { Width = 180 }
    ];

    private static FlowLayoutPanel GoalRow(string title, TextBox[] boxes) =>
        Row(L(title), L("Z1"), boxes[0], L("Z2"), boxes[1]);

    private static Label L(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 8, 3, 3)
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 2, 3, 8),
        MaximumSize = new Size(900, 0)
    };

    private static Label Section(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Control.DefaultFont, FontStyle.Bold),
        Margin = new Padding(3, 10, 3, 4)
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 2, 0, 2)
        };
        foreach (var c in controls)
        {
            panel.Controls.Add(c);
        }

        return panel;
    }

    private static Button Btn(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
        b.Click += onClick;
        return b;
    }
}
