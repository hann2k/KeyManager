namespace KeyManager.App;

/// <summary>마스터 암호 변경: 현재/새/확인 입력.</summary>
internal sealed class ChangeMasterPasswordForm : Form
{
    private readonly TextBox _current = new() { UseSystemPasswordChar = true, Width = 240, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _new = new() { UseSystemPasswordChar = true, Width = 240, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true, Width = 240, Anchor = AnchorStyles.Left | AnchorStyles.Right };

    public string CurrentPassword => _current.Text;
    public string NewPassword => _new.Text;

    public ChangeMasterPasswordForm()
    {
        DialogUi.ConfigureDialog(this, Loc.T("cpw.title"));
        StartPosition = FormStartPosition.CenterScreen;

        var grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(grid, 0, Loc.T("cpw.current"), _current);
        AddRow(grid, 1, Loc.T("cpw.new"), _new);
        AddRow(grid, 2, Loc.T("cpw.confirm"), _confirm);

        var ok = new Button { Text = Loc.T("save"), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Loc.T("cancel"), DialogResult = DialogResult.Cancel };
        ok.Click += OnOk;
        var buttons = DialogUi.ButtonRow(ok, cancel);
        grid.Controls.Add(buttons, 0, 3);
        grid.SetColumnSpan(buttons, 2);

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
        if (CurrentPassword.Length == 0 || NewPassword.Length == 0 || _confirm.Text.Length == 0)
        {
            MessageBox.Show(Loc.T("cpw.errEmpty"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        if (NewPassword != _confirm.Text)
        {
            MessageBox.Show(Loc.T("cpw.errMismatch"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }
}
