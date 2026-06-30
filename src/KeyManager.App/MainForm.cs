using KeyManager.Core;

namespace KeyManager.App;

/// <summary>키·클라이언트 관리 UI. 목록엔 이름만 표시(설계 §12). 언어는 Loc 기준으로 빌드된다.</summary>
internal sealed class MainForm : Form
{
    private readonly VaultStore _store;
    private readonly Action<Lang> _onLanguageChange;
    private readonly TreeView _secretTree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly ListBox _clientList = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public MainForm(VaultStore store, Action<Lang> onLanguageChange)
    {
        _store = store;
        _onLanguageChange = onLanguageChange;
        Text = Loc.T("app.title");
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(480, 440);
        MinimumSize = new Size(400, 340);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildSecretsTab());
        tabs.TabPages.Add(BuildClientsTab());
        tabs.TabPages.Add(BuildLanguageTab());
        Controls.Add(tabs);

        RefreshSecrets();
        RefreshClients();
    }

    private TabPage BuildSecretsTab()
    {
        var page = new TabPage(Loc.T("tab.keys"));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(new Label
        {
            Text = Loc.T("keys.legend"),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText,
        }, 0, 0);

        layout.Controls.Add(_secretTree, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), AutoSize = true };
        AddButton(buttons, Loc.T("btn.add"), AddSecret);
        AddButton(buttons, Loc.T("btn.changeValue"), ChangeSecret);
        AddButton(buttons, Loc.T("btn.description"), EditDescription);
        AddButton(buttons, Loc.T("btn.deleteKey"), DeleteSecret);
        AddButton(buttons, Loc.T("btn.deleteGroup"), DeleteGroupSecrets);
        layout.Controls.Add(buttons, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildClientsTab()
    {
        var page = new TabPage(Loc.T("tab.clients"));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(_clientList, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), AutoSize = true };
        AddButton(buttons, Loc.T("btn.register"), AddClient);
        AddButton(buttons, Loc.T("btn.editPerm"), EditClient);
        AddButton(buttons, Loc.T("btn.delete"), DeleteClient);
        layout.Controls.Add(buttons, 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildLanguageTab()
    {
        var page = new TabPage(Loc.T("tab.language"));
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12), WrapContents = false };

        layout.Controls.Add(new Label { Text = Loc.T("langtab.info"), AutoSize = true, Margin = new Padding(3, 3, 3, 10) });

        var en = new RadioButton { Text = Loc.T("lang.english"), AutoSize = true, Checked = Loc.Current == Lang.En, Margin = new Padding(3, 4, 3, 4) };
        var ko = new RadioButton { Text = Loc.T("lang.korean"), AutoSize = true, Checked = Loc.Current == Lang.Ko, Margin = new Padding(3, 4, 3, 4) };
        en.CheckedChanged += (_, _) => { if (en.Checked && Loc.Current != Lang.En) _onLanguageChange(Lang.En); };
        ko.CheckedChanged += (_, _) => { if (ko.Checked && Loc.Current != Lang.Ko) _onLanguageChange(Lang.Ko); };
        layout.Controls.Add(en);
        layout.Controls.Add(ko);

        page.Controls.Add(layout);
        return page;
    }

    private static void AddButton(Control parent, string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(75, 0) };
        b.Click += (_, _) => onClick();
        parent.Controls.Add(b);
    }

    // ---- 키 ----

    private bool _secretsLoadedOnce;

    private void RefreshSecrets()
    {
        var expanded = KeyTreeBuilder.CaptureExpanded(_secretTree);
        var selected = _secretTree.SelectedNode?.Tag as string;

        _secretTree.Nodes.Clear();
        if (!_store.IsUnlocked) return;

        var names = _store.ListSecretNames();
        KeyTreeBuilder.Populate(_secretTree, names, expandAll: !_secretsLoadedOnce);
        SetTooltips(_secretTree.Nodes, names.ToHashSet(StringComparer.Ordinal));
        if (_secretsLoadedOnce) KeyTreeBuilder.RestoreState(_secretTree, expanded, selected);
        _secretsLoadedOnce = true;
    }

    /// <summary>노드 툴팁에 설명을 표시(키 설명 / 그룹 설명).</summary>
    private void SetTooltips(TreeNodeCollection coll, HashSet<string> keySet)
    {
        foreach (TreeNode n in coll)
        {
            string path = (string)n.Tag!;
            string? desc = keySet.Contains(path) ? _store.GetSecretDescription(path) : _store.GetGroupDescription(path);
            if (!string.IsNullOrEmpty(desc)) n.ToolTipText = desc;
            SetTooltips(n.Nodes, keySet);
        }
    }

    private string? SelectedSecretName()
    {
        if (_secretTree.SelectedNode?.Tag is not string path) return null;
        return _store.ListSecretNames().Contains(path) ? path : null;
    }

    private string? SelectedNodePath() => _secretTree.SelectedNode?.Tag as string;

    private void AddSecret()
    {
        using var f = new AddSecretForm();
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Guard(() =>
        {
            _store.AddOrUpdateSecret(f.SecretName, f.SecretValue);
            if (f.DescriptionText.Length > 0) _store.SetSecretDescription(f.SecretName, f.DescriptionText);
        });
        RefreshSecrets();
    }

    private void ChangeSecret()
    {
        if (SelectedSecretName() is not string name)
        {
            MessageBox.Show(Loc.T("msg.selectLeafChange"), Loc.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string? current = _store.GetSecretDescription(name);
        using var f = new AddSecretForm(name, current);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Guard(() =>
        {
            _store.AddOrUpdateSecret(name, f.SecretValue);
            _store.SetSecretDescription(name, f.DescriptionText);
        });
        RefreshSecrets();
    }

    private void EditDescription()
    {
        if (SelectedNodePath() is not string path)
        {
            MessageBox.Show(Loc.T("msg.selectNode"), Loc.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        bool isKey = _store.ListSecretNames().Contains(path);
        string? current = isKey ? _store.GetSecretDescription(path) : _store.GetGroupDescription(path);
        using var f = new DescriptionForm(path, !isKey, current);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Guard(() =>
        {
            if (isKey) _store.SetSecretDescription(path, f.Description);
            else _store.SetGroupDescription(path, f.Description);
        });
        RefreshSecrets();
    }

    private void DeleteSecret()
    {
        if (SelectedSecretName() is not string name)
        {
            MessageBox.Show(Loc.T("msg.selectLeafDelete"), Loc.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(Loc.T("confirm.deleteKey", name), Loc.T("title.delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Guard(() => _store.DeleteSecret(name));
        RefreshSecrets();
    }

    private void DeleteGroupSecrets()
    {
        if (SelectedNodePath() is not string prefix)
        {
            MessageBox.Show(Loc.T("msg.selectGroup"), Loc.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int count = _store.CountGroup(prefix);
        if (count == 0) return;
        if (MessageBox.Show(Loc.T("confirm.deleteGroup", prefix, count), Loc.T("title.deleteGroup"),
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
        Guard(() => _store.UpdateClientAllowedKeys(name, f.AllowedKeys));
        RefreshClients();
    }

    private void DeleteClient()
    {
        if (_clientList.SelectedItem is not string name) return;
        if (MessageBox.Show(Loc.T("confirm.deleteClient", name), Loc.T("title.delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Guard(() => _store.DeleteClient(name));
        RefreshClients();
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, Loc.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) { MessageBox.Show(Loc.T("err.fmt", ex.Message), Loc.T("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
