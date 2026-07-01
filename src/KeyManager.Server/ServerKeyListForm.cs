using System.Drawing;
using System.Globalization;
using KeyManager.App; // Loc (linked)

namespace KeyManager.Server;

/// <summary>
/// "Key 목록 보기" 읽기전용 창(설계 §9). 서버는 Kd가 없어 키 이름을 못 본다 →
/// 봉투(소비앱) 이름 + UpdatedAt, 금고 존재 여부, 봉투 수만 표시. 열 때마다 새로 고침.
/// </summary>
internal sealed class ServerKeyListForm : Form
{
    private readonly ServerStore _store;
    private readonly Label _vaultLabel = new() { AutoSize = true, Margin = new Padding(3, 3, 3, 2) };
    private readonly Label _countLabel = new() { AutoSize = true, Margin = new Padding(3, 2, 3, 6) };
    private readonly ListView _grid = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        UseCompatibleStateImageBehavior = false,
    };
    private readonly ColumnHeader _colClient = new();
    private readonly ColumnHeader _colUpdated = new();

    public ServerKeyListForm(ServerStore store)
    {
        _store = store;
        Text = Loc.T("srv.list.title");
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(520, 400);
        MinimumSize = new Size(400, 300);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // note
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // vault present
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // count
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // grid
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons

        layout.Controls.Add(new Label
        {
            Text = Loc.T("srv.list.note"),
            AutoSize = true, MaximumSize = new Size(480, 0),
            ForeColor = SystemColors.GrayText, Margin = new Padding(3, 3, 3, 8),
        }, 0, 0);

        layout.Controls.Add(_vaultLabel, 0, 1);
        layout.Controls.Add(_countLabel, 0, 2);

        _colClient.Text = Loc.T("srv.list.colClient");
        _colClient.Width = 260;
        _colUpdated.Text = Loc.T("srv.list.colUpdated");
        _colUpdated.Width = 220;
        _grid.Columns.Add(_colClient);
        _grid.Columns.Add(_colUpdated);
        layout.Controls.Add(_grid, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0) };
        var close = new Button { Text = Loc.T("close"), AutoSize = true, MinimumSize = new Size(75, 0) };
        close.Click += (_, _) => Close();
        var refresh = new Button { Text = Loc.T("srv.list.refresh"), AutoSize = true, MinimumSize = new Size(75, 0) };
        refresh.Click += (_, _) => Reload();
        buttons.Controls.Add(close);
        buttons.Controls.Add(refresh);
        layout.Controls.Add(buttons, 0, 4);

        Controls.Add(layout);
        Reload();
    }

    private void Reload()
    {
        ServerMetadata meta = _store.GetMetadata();
        _vaultLabel.Text = meta.MasterVaultPresent ? Loc.T("srv.list.vaultPresent") : Loc.T("srv.list.vaultAbsent");
        _countLabel.Text = Loc.T("srv.list.envCount", meta.EnvelopeCount);

        _grid.BeginUpdate();
        _grid.Items.Clear();
        foreach (EnvelopeMeta e in meta.Envelopes)
        {
            var item = new ListViewItem(e.Client);
            item.SubItems.Add(FormatTime(e.UpdatedAt));
            _grid.Items.Add(item);
        }
        _grid.EndUpdate();

        if (meta.Envelopes.Count == 0)
            _grid.Items.Add(new ListViewItem(Loc.T("srv.list.empty")) { ForeColor = SystemColors.GrayText });
    }

    /// <summary>저장된 ISO-8601(UTC)을 로컬 서버 시간 yyyy-MM-dd HH:mm:ss.fff로 표시.</summary>
    private static string FormatTime(string iso)
    {
        if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        return iso; // 파싱 실패 시 원본 그대로
    }
}
