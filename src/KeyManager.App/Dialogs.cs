namespace KeyManager.App;

/// <summary>다이얼로그 공통 헬퍼.</summary>
internal static class DialogUi
{
    /// <summary>오른쪽 정렬 버튼 줄.</summary>
    public static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 10, 3, 3),
        };
        foreach (var b in buttons)
        {
            b.AutoSize = true;
            b.MinimumSize = new Size(75, 0);
            panel.Controls.Add(b);
        }
        return panel;
    }

    public static void ConfigureDialog(Form f, string title)
    {
        f.Text = title;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.StartPosition = FormStartPosition.CenterParent;
        f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
        f.AutoScaleMode = AutoScaleMode.Font;
        f.AutoSize = true;
        f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        f.Padding = new Padding(12);
    }
}

/// <summary>키 추가/수정. 값은 입력하는 이 순간에만 평문으로 보인다(설계 §12).</summary>
internal sealed class AddSecretForm : Form
{
    private readonly TextBox _name = new() { Width = 300, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _value = new() { Width = 300, Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CheckBox _reveal = new() { Text = "값 표시", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };

    public string SecretName => _name.Text.Trim();
    public string SecretValue => _value.Text;

    public AddSecretForm(string? fixedName = null)
    {
        DialogUi.ConfigureDialog(this, fixedName is null ? "키 추가" : $"값 변경 — {fixedName}");

        var grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        grid.Controls.Add(new Label { Text = "이름:", AutoSize = true, Margin = new Padding(3, 3, 3, 1) });
        if (fixedName is not null) { _name.Text = fixedName; _name.ReadOnly = true; }
        grid.Controls.Add(_name);

        grid.Controls.Add(new Label { Text = "값:", AutoSize = true, Margin = new Padding(3, 8, 3, 1) });
        _value.UseSystemPasswordChar = true;
        grid.Controls.Add(_value);

        _reveal.CheckedChanged += (_, _) => _value.UseSystemPasswordChar = !_reveal.Checked;
        grid.Controls.Add(_reveal);

        var ok = new Button { Text = "저장", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel };
        ok.Click += (s, e) =>
        {
            if (SecretName.Length == 0 || SecretValue.Length == 0)
            {
                MessageBox.Show("이름과 값을 모두 입력하세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        };
        grid.Controls.Add(DialogUi.ButtonRow(ok, cancel));

        Controls.Add(grid);
        AcceptButton = ok; CancelButton = cancel;
    }
}

/// <summary>
/// 클라이언트(소비 앱) 등록/편집. 이름 + 접근 허용 키를 그룹 트리로 선택.
/// 부모(그룹) 노드를 체크하면 그 그룹 전체 권한(콜론 prefix)이 부여된다.
/// </summary>
internal sealed class AddClientForm : Form
{
    private readonly TextBox _name = new() { Width = 320, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TreeView _tree = new()
    {
        Width = 320, Height = 200, CheckBoxes = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
    };
    private bool _suppressCheck;

    public string ClientName => _name.Text.Trim();
    public string[] AllowedKeys => KeyTreeBuilder.CollectGrants(_tree).ToArray();

    /// <param name="existingName">편집 모드면 기존 이름(읽기전용). 신규 등록이면 null.</param>
    /// <param name="preselected">편집 모드에서 미리 체크할 grant들.</param>
    public AddClientForm(IEnumerable<string> availableKeys, string? existingName = null, IEnumerable<string>? preselected = null)
    {
        bool edit = existingName is not null;
        DialogUi.ConfigureDialog(this, edit ? $"권한 편집 — {existingName}" : "클라이언트 등록");

        var grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        grid.Controls.Add(new Label { Text = "클라이언트 이름:", AutoSize = true, Margin = new Padding(3, 3, 3, 1) });
        if (edit) { _name.Text = existingName; _name.ReadOnly = true; }
        grid.Controls.Add(_name);

        grid.Controls.Add(new Label { Text = "접근 허용 키 (그룹 체크 = 하위 전체):", AutoSize = true, Margin = new Padding(3, 8, 3, 1) });
        var pre = preselected is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(preselected, StringComparer.Ordinal);
        KeyTreeBuilder.Populate(_tree, availableKeys, pre);
        _tree.AfterCheck += OnAfterCheck; // 부모 체크 → 하위 전파
        grid.Controls.Add(_tree);

        var ok = new Button { Text = edit ? "저장" : "등록", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel };
        ok.Click += (s, e) =>
        {
            if (ClientName.Length == 0)
            {
                MessageBox.Show("클라이언트 이름을 입력하세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        };
        grid.Controls.Add(DialogUi.ButtonRow(ok, cancel));

        Controls.Add(grid);
        AcceptButton = ok; CancelButton = cancel;
    }

    private void OnAfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (_suppressCheck || e.Node is null) return;
        if (e.Action == TreeViewAction.Unknown) return; // 프로그램적 설정은 무시(전파 중복 방지)
        _suppressCheck = true;
        KeyTreeBuilder.CheckRecursive(e.Node, e.Node.Checked);
        _suppressCheck = false;
    }
}

/// <summary>등록 직후 시드 S를 1회만 표시(설계 §6.2). 소비 앱 설정에 복사해 넣는다.</summary>
internal sealed class SeedDisplayForm : Form
{
    public SeedDisplayForm(string clientName, string base64Seed)
    {
        DialogUi.ConfigureDialog(this, $"'{clientName}' 시드 (1회만 표시)");

        var grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        grid.Controls.Add(new Label
        {
            Text = "아래 시드를 소비 앱에 설정하세요. 이 창을 닫으면 다시 볼 수 없습니다.",
            AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 3, 3, 8),
        });

        var box = new TextBox
        {
            Text = base64Seed, ReadOnly = true, Width = 420,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(FontFamily.GenericMonospace, 9),
        };
        box.SelectAll();
        grid.Controls.Add(box);

        var copy = new Button { Text = "클립보드로 복사", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        copy.Click += (_, _) => { Clipboard.SetText(base64Seed); copy.Text = "복사됨!"; };
        grid.Controls.Add(copy);

        var ok = new Button { Text = "닫기", DialogResult = DialogResult.OK };
        grid.Controls.Add(DialogUi.ButtonRow(ok));

        Controls.Add(grid);
        AcceptButton = ok;
    }
}
