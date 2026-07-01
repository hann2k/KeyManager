using System.Drawing;

namespace KeyManager.App;

/// <summary>마스터 암호 입력. create 모드면 확인 입력까지 받는다. (고DPI 대응: AutoSize 레이아웃)</summary>
internal sealed class MasterPasswordForm : Form
{
    private readonly TextBox _pw = new() { UseSystemPasswordChar = true, Width = 240, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true, Width = 240, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly bool _create;

    public string Password => _pw.Text;

    public MasterPasswordForm(bool create)
    {
        _create = create;
        Text = Loc.T(create ? "mp.titleCreate" : "mp.titleUnlock");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

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
            Text = Loc.T(create ? "mp.infoCreate" : "mp.infoUnlock"),
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 6),
        };
        grid.Controls.Add(info, 0, r);
        grid.SetColumnSpan(info, 2);
        r++;

        // 경고 문구(눈에 띄게)
        var warn = new Label
        {
            Text = Loc.T("warn.master"),
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            ForeColor = Color.Firebrick,
            Margin = new Padding(3, 3, 3, 10),
        };
        grid.Controls.Add(warn, 0, r);
        grid.SetColumnSpan(warn, 2);
        r++;

        grid.Controls.Add(new Label { Text = Loc.T("mp.password"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) }, 0, r);
        grid.Controls.Add(_pw, 1, r);
        r++;

        if (create)
        {
            grid.Controls.Add(new Label { Text = Loc.T("mp.confirm"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) }, 0, r);
            grid.Controls.Add(_confirm, 1, r);
            r++;
        }

        var ok = new Button { Text = Loc.T("ok"), DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(75, 0) };
        var cancel = new Button { Text = Loc.T("cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(75, 0) };
        ok.Click += OnOk;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 10, 3, 3) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        grid.Controls.Add(buttons, 0, r);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_pw.Text))
        {
            MessageBox.Show(Loc.T("mp.errEmpty"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        if (_create && _pw.Text != _confirm.Text)
        {
            MessageBox.Show(Loc.T("mp.errMismatch"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }
}
