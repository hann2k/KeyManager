using System.Drawing;
using KeyManager.App; // Loc (linked)

namespace KeyManager.Server;

/// <summary>
/// 서버 최초 실행 시 admin 토큰 A를 1회만 보여주는 창(설계 §9, App의 시드 1회 표시 UX 미러).
/// 닫으면 다시 볼 수 없다. 클립보드 복사 버튼 제공.
/// </summary>
internal sealed class AdminTokenForm : Form
{
    public AdminTokenForm(string tokenBase64, int port)
    {
        Text = Loc.T("srv.token.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Icon = null;

        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        grid.Controls.Add(new Label
        {
            Text = Loc.T("srv.token.info"),
            AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(3, 3, 3, 8),
        });

        grid.Controls.Add(new Label
        {
            Text = Loc.T("srv.token.port") + " " + port,
            AutoSize = true, Margin = new Padding(3, 3, 3, 8),
        });

        grid.Controls.Add(new Label { Text = Loc.T("srv.token.label"), AutoSize = true, Margin = new Padding(3, 3, 3, 2) });

        var box = new TextBox
        {
            Text = tokenBase64, ReadOnly = true, Width = 440,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(FontFamily.GenericMonospace, 9),
        };
        box.SelectAll();
        grid.Controls.Add(box);

        var copy = new Button { Text = Loc.T("srv.token.copy"), AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        copy.Click += (_, _) => { Clipboard.SetText(tokenBase64); copy.Text = Loc.T("srv.token.copied"); };
        grid.Controls.Add(copy);

        var ok = new Button { Text = Loc.T("close"), DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(75, 0), Margin = new Padding(3, 10, 3, 3), Anchor = AnchorStyles.Right };
        grid.Controls.Add(ok);

        Controls.Add(grid);
        AcceptButton = ok;
    }
}
