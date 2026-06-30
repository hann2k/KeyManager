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
        Text = create ? "마스터 암호 설정" : "잠금 해제";
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

        var info = new Label
        {
            Text = create
                ? "새 vault를 만듭니다. 마스터 암호를 정하세요.\n분실 시 복구할 수 없습니다."
                : "마스터 암호를 입력하세요.",
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 10),
        };
        grid.Controls.Add(info, 0, 0);
        grid.SetColumnSpan(info, 2);

        grid.Controls.Add(new Label { Text = "암호:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) }, 0, 1);
        grid.Controls.Add(_pw, 1, 1);

        int buttonRow = 2;
        if (create)
        {
            grid.Controls.Add(new Label { Text = "확인:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) }, 0, 2);
            grid.Controls.Add(_confirm, 1, 2);
            buttonRow = 3;
        }

        var ok = new Button { Text = "확인", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(75, 0) };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(75, 0) };
        ok.Click += OnOk;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 10, 3, 3) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        grid.Controls.Add(buttons, 0, buttonRow);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_pw.Text))
        {
            MessageBox.Show("암호를 입력하세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        if (_create && _pw.Text != _confirm.Text)
        {
            MessageBox.Show("확인 암호가 일치하지 않습니다.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }
}
