namespace KeyManager.App;

/// <summary>최초 실행 시 1회 표시되는 언어 선택 창(이중 언어 중립 표기).</summary>
internal sealed class LanguageSelectForm : Form
{
    private readonly RadioButton _en = new() { Text = "English", AutoSize = true, Checked = true, Margin = new Padding(3, 4, 3, 4) };
    private readonly RadioButton _ko = new() { Text = "한국어", AutoSize = true, Margin = new Padding(3, 4, 3, 4) };

    public Lang Selected => _ko.Checked ? Lang.Ko : Lang.En;

    public LanguageSelectForm()
    {
        Text = "Language / 언어";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        grid.Controls.Add(new Label { Text = "Select a language. / 언어를 선택하세요.", AutoSize = true, Margin = new Padding(3, 3, 3, 10) });
        grid.Controls.Add(_en);
        grid.Controls.Add(_ko);

        var ok = new Button { Text = "OK / 확인", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(90, 0), Margin = new Padding(3, 12, 3, 3), Anchor = AnchorStyles.Right };
        grid.Controls.Add(ok);

        Controls.Add(grid);
        AcceptButton = ok;
    }
}
