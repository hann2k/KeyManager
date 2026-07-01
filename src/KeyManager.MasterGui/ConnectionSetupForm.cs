using System.Drawing;
using KeyManager.App; // Loc (linked)

namespace KeyManager.MasterGui;

/// <summary>
/// 최초 실행 시 서버 접속 설정(설계 §8): 호스트/포트/admin 토큰 A 붙여넣기.
/// 확인 시 값을 검증하고 <see cref="Host"/>/<see cref="Port"/>/<see cref="TokenBase64"/>로 노출.
/// </summary>
internal sealed class ConnectionSetupForm : Form
{
    private readonly TextBox _host = new() { Width = 260, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _port = new() { Width = 260, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _token = new() { Width = 260, Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font(FontFamily.GenericMonospace, 9) };

    public string Host => _host.Text.Trim();
    public int Port { get; private set; }
    public string TokenBase64 => _token.Text.Trim();

    public ConnectionSetupForm(string host, int port, string tokenBase64)
    {
        Text = Loc.T("mg.setup.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _host.Text = host;
        _port.Text = port.ToString();
        _token.Text = tokenBase64;

        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int r = 0;
        var info = new Label
        {
            Text = Loc.T("mg.setup.info"),
            AutoSize = true, MaximumSize = new Size(360, 0), Margin = new Padding(3, 3, 3, 10),
        };
        grid.Controls.Add(info, 0, r); grid.SetColumnSpan(info, 2); r++;

        AddRow(grid, r++, Loc.T("mg.setup.host"), _host);
        AddRow(grid, r++, Loc.T("mg.setup.port"), _port);
        AddRow(grid, r++, Loc.T("mg.setup.token"), _token);

        var ok = new Button { Text = Loc.T("ok"), DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(75, 0) };
        var cancel = new Button { Text = Loc.T("cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(75, 0) };
        ok.Click += OnOk;
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 10, 3, 3) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        grid.Controls.Add(buttons, 0, r); grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = ok; CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel grid, int row, string label, TextBox box)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) }, 0, row);
        grid.Controls.Add(box, 1, row);
    }

    private void OnOk(object? sender, EventArgs e)
    {
        if (Host.Length == 0)
        {
            Fail(Loc.T("mg.setup.errHost")); return;
        }
        if (!int.TryParse(_port.Text.Trim(), out int p) || p is < 1 or > 65535)
        {
            Fail(Loc.T("mg.setup.errPort")); return;
        }
        if (!TryDecodeToken(TokenBase64))
        {
            Fail(Loc.T("mg.setup.errToken")); return;
        }
        Port = p;
    }

    private static bool TryDecodeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        try { return Convert.FromBase64String(token).Length > 0; }
        catch (FormatException) { return false; }
    }

    private void Fail(string message)
    {
        MessageBox.Show(message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        DialogResult = DialogResult.None;
    }
}
