using KeyManager.Core;

namespace KeyManager.App;

/// <summary>키·클라이언트 관리 UI. 목록엔 이름만 표시(설계 §12).</summary>
internal sealed class MainForm : Form
{
    private readonly VaultStore _store;
    private readonly TreeView _secretTree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly ListBox _clientList = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public MainForm(VaultStore store)
    {
        _store = store;
        Text = "KeyManager";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font; // 고DPI 스케일
        ClientSize = new Size(480, 420);
        MinimumSize = new Size(400, 320);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildSecretsTab());
        tabs.TabPages.Add(BuildClientsTab());
        Controls.Add(tabs);

        RefreshSecrets();
        RefreshClients();
    }

    private TabPage BuildSecretsTab()
    {
        var page = new TabPage("키");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(new Label
        {
            Text = "●  = 값을 가진 키 (마커 없으면 그룹)",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText,
        }, 0, 0);

        layout.Controls.Add(_secretTree, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), AutoSize = true };
        var add = new Button { Text = "추가", AutoSize = true, MinimumSize = new Size(75, 0) };
        var change = new Button { Text = "값 변경", AutoSize = true, MinimumSize = new Size(75, 0) };
        var del = new Button { Text = "키 삭제", AutoSize = true, MinimumSize = new Size(75, 0) };
        var delGroup = new Button { Text = "그룹 삭제", AutoSize = true, MinimumSize = new Size(80, 0) };
        add.Click += (_, _) => AddSecret();
        change.Click += (_, _) => ChangeSecret();
        del.Click += (_, _) => DeleteSecret();
        delGroup.Click += (_, _) => DeleteGroupSecrets();
        buttons.Controls.Add(add);
        buttons.Controls.Add(change);
        buttons.Controls.Add(del);
        buttons.Controls.Add(delGroup);
        layout.Controls.Add(buttons, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildClientsTab()
    {
        var page = new TabPage("클라이언트");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(_clientList, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), AutoSize = true };
        var add = new Button { Text = "등록", AutoSize = true, MinimumSize = new Size(75, 0) };
        var edit = new Button { Text = "권한 편집", AutoSize = true, MinimumSize = new Size(75, 0) };
        var del = new Button { Text = "삭제", AutoSize = true, MinimumSize = new Size(75, 0) };
        add.Click += (_, _) => AddClient();
        edit.Click += (_, _) => EditClient();
        del.Click += (_, _) => DeleteClient();
        buttons.Controls.Add(add);
        buttons.Controls.Add(edit);
        buttons.Controls.Add(del);
        layout.Controls.Add(buttons, 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    // ---- 키 ----

    private bool _secretsLoadedOnce;

    private void RefreshSecrets()
    {
        // 펼침/선택 상태 기억 후 재구성 → 복원.
        var expanded = KeyTreeBuilder.CaptureExpanded(_secretTree);
        var selected = _secretTree.SelectedNode?.Tag as string;

        _secretTree.Nodes.Clear();
        if (!_store.IsUnlocked) return;

        KeyTreeBuilder.Populate(_secretTree, _store.ListSecretNames(), expandAll: !_secretsLoadedOnce);
        if (_secretsLoadedOnce) KeyTreeBuilder.RestoreState(_secretTree, expanded, selected);
        _secretsLoadedOnce = true;
    }

    /// <summary>선택 노드가 실제 키(값을 가짐)면 그 전체 이름, 아니면 null.</summary>
    private string? SelectedSecretName()
    {
        if (_secretTree.SelectedNode?.Tag is not string path) return null;
        return _store.ListSecretNames().Contains(path) ? path : null;
    }

    /// <summary>선택 노드의 전체 경로(키든 그룹이든).</summary>
    private string? SelectedNodePath() => _secretTree.SelectedNode?.Tag as string;

    private void AddSecret()
    {
        using var f = new AddSecretForm();
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Guard(() => _store.AddOrUpdateSecret(f.SecretName, f.SecretValue));
        RefreshSecrets();
    }

    private void ChangeSecret()
    {
        if (SelectedSecretName() is not string name)
        {
            MessageBox.Show("값을 변경할 키(잎 항목)를 선택하세요.", "KeyManager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var f = new AddSecretForm(name);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Guard(() => _store.AddOrUpdateSecret(name, f.SecretValue));
        RefreshSecrets();
    }

    private void DeleteSecret()
    {
        if (SelectedSecretName() is not string name)
        {
            MessageBox.Show("삭제할 키(잎 항목)를 선택하세요.", "KeyManager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show($"'{name}' 키를 삭제할까요?", "삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Guard(() => _store.DeleteSecret(name));
        RefreshSecrets();
    }

    private void DeleteGroupSecrets()
    {
        if (SelectedNodePath() is not string prefix)
        {
            MessageBox.Show("삭제할 그룹(또는 키)을 선택하세요.", "KeyManager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int count = _store.CountGroup(prefix);
        if (count == 0) return;
        if (MessageBox.Show($"'{prefix}' 그룹의 {count}개 키를 모두 삭제할까요?\n(하위 전체 포함)", "그룹 삭제",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        Guard(() => _store.DeleteGroup(prefix));
        RefreshSecrets();
    }

    // ---- 클라이언트 ----

    private void RefreshClients()
    {
        _clientList.Items.Clear();
        if (!_store.IsUnlocked) return;
        foreach (var n in _store.ListClientNames()) _clientList.Items.Add(n);
    }

    private void AddClient()
    {
        using var f = new AddClientForm(_store.ListSecretNames());
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Guard(() =>
        {
            byte[] seed = _store.AddClient(f.ClientName, f.AllowedKeys);
            using var sf = new SeedDisplayForm(f.ClientName, Convert.ToBase64String(seed));
            sf.ShowDialog(this);
            Array.Clear(seed);
        });
        RefreshClients();
    }

    private void EditClient()
    {
        if (_clientList.SelectedItem is not string name) return;
        if (!_store.TryGetClient(name, out var view) || view is null) return;
        using var f = new AddClientForm(_store.ListSecretNames(), name, view.AllowedKeys);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Guard(() => _store.UpdateClientAllowedKeys(name, f.AllowedKeys)); // 시드 유지, 권한만 교체
        RefreshClients();
    }

    private void DeleteClient()
    {
        if (_clientList.SelectedItem is not string name) return;
        if (MessageBox.Show($"'{name}' 클라이언트를 삭제할까요?", "삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Guard(() => _store.DeleteClient(name));
        RefreshClients();
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "KeyManager", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) { MessageBox.Show($"오류: {ex.Message}", "KeyManager", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
