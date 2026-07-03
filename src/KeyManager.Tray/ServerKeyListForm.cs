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
    // 트레이 동반앱은 서버와 별도 프로세스라, 열 때·새로 고침 때마다 디스크에서 스냅샷을 다시 읽는다
    // (서버가 push로 갱신한 최신을 반영). 서버 미기동/데이터 없음이면 null.
    private readonly Func<ServerMetadata?> _metadata;
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

    public ServerKeyListForm(Func<ServerMetadata?> metadata)
    {
        _metadata = metadata;
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
        ServerMetadata? meta = _metadata();

        _grid.BeginUpdate();
        _grid.Items.Clear();

        if (meta is null)
        {
            // 서버가 아직 스토어를 만들지 않음(미기동/데이터 없음).
            _vaultLabel.Text = Loc.T("srv.list.vaultAbsent");
            _countLabel.Text = Loc.T("srv.list.envCount", 0);
            _grid.Items.Add(new ListViewItem(Loc.T("srv.list.notRunning")) { ForeColor = SystemColors.GrayText });
            _grid.EndUpdate();
            return;
        }

        _vaultLabel.Text = meta.MasterVaultPresent ? Loc.T("srv.list.vaultPresent") : Loc.T("srv.list.vaultAbsent");
        _countLabel.Text = Loc.T("srv.list.envCount", meta.EnvelopeCount);

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
