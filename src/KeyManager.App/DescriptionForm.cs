namespace KeyManager.App;

/// <summary>키 또는 그룹의 설명을 편집하는 작은 창.</summary>
internal sealed class DescriptionForm : Form
{
    private readonly TextBox _desc = new()
    {
        Width = 360, Multiline = true, Height = 100, ScrollBars = ScrollBars.Vertical,
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
    };

    public string Description => _desc.Text.Trim();

    /// <param name="isGroup">그룹 설명이면 true.</param>
    public DescriptionForm(string nodePath, bool isGroup, string? current)
    {
        DialogUi.ConfigureDialog(this, Loc.T(isGroup ? "desc.titleGroup" : "desc.titleKey", nodePath));

        var grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        grid.Controls.Add(new Label { Text = Loc.T("desc.label"), AutoSize = true, Margin = new Padding(3, 3, 3, 1) });
        _desc.Text = current ?? "";
        grid.Controls.Add(_desc);

        var ok = new Button { Text = Loc.T("save"), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Loc.T("cancel"), DialogResult = DialogResult.Cancel };
        grid.Controls.Add(DialogUi.ButtonRow(ok, cancel));

        Controls.Add(grid);
        AcceptButton = ok; CancelButton = cancel;
    }
}
