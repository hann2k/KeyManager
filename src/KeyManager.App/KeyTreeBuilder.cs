namespace KeyManager.App;

/// <summary>
/// 콜론(:) 계층 키 이름들을 TreeView로 펼치고, 체크/펼침 상태를 변환·복원한다.
/// 각 노드 Tag = 전체 콜론 경로. 값을 가진 노드(실제 키)는 '●' 마커로 표시해
/// "키이자 그룹"인 경우(같은 이름)도 구별되게 한다.
/// </summary>
internal static class KeyTreeBuilder
{
    public const char Sep = ':';
    public const string KeyMarker = "● ";

    /// <summary>키 이름들로 트리를 채운다.</summary>
    public static void Populate(TreeView tv, IEnumerable<string> keyNames, ISet<string>? checkedPaths = null, bool expandAll = true)
    {
        var keys = keyNames as ICollection<string> ?? keyNames.ToList();
        var keySet = new HashSet<string>(keys, StringComparer.Ordinal);

        tv.BeginUpdate();
        tv.Nodes.Clear();
        var byPath = new Dictionary<string, TreeNode>(StringComparer.Ordinal);

        foreach (var name in keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            string[] parts = name.Split(Sep);
            string path = "";
            var coll = tv.Nodes;
            for (int i = 0; i < parts.Length; i++)
            {
                path = i == 0 ? parts[0] : path + Sep + parts[i];
                if (!byPath.TryGetValue(path, out var node))
                {
                    node = new TreeNode(parts[i]) { Tag = path };
                    coll.Add(node);
                    byPath[path] = node;
                }
                coll = node.Nodes;
            }
        }

        // 마커: 값을 가진 노드(실제 키)에 '●'. (툴팁/설명은 호출자가 설정)
        foreach (var (path, node) in byPath)
        {
            string seg = path.Contains(Sep) ? path[(path.LastIndexOf(Sep) + 1)..] : path;
            bool isKey = keySet.Contains(path);
            node.Text = isKey ? KeyMarker + seg : seg;
        }

        tv.ShowNodeToolTips = true;
        if (checkedPaths is not null) ApplyChecks(tv.Nodes, checkedPaths);
        if (expandAll) tv.ExpandAll();
        tv.EndUpdate();
    }

    public static void CheckRecursive(TreeNode node, bool value)
    {
        node.Checked = value;
        foreach (TreeNode c in node.Nodes) CheckRecursive(c, value);
    }

    /// <summary>최소 grant 집합: 체크된 노드 중 최상위들(부모가 체크되면 자식은 생략).</summary>
    public static List<string> CollectGrants(TreeView tv)
    {
        var result = new List<string>();
        Walk(tv.Nodes, result);
        return result;

        static void Walk(TreeNodeCollection coll, List<string> acc)
        {
            foreach (TreeNode n in coll)
            {
                if (n.Checked) acc.Add((string)n.Tag!);
                else Walk(n.Nodes, acc);
            }
        }
    }

    /// <summary>현재 펼쳐진 노드들의 경로.</summary>
    public static HashSet<string> CaptureExpanded(TreeView tv)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        Walk(tv.Nodes);
        return set;

        void Walk(TreeNodeCollection coll)
        {
            foreach (TreeNode n in coll)
            {
                if (n.IsExpanded) set.Add((string)n.Tag!);
                Walk(n.Nodes);
            }
        }
    }

    /// <summary>펼침 상태와 선택을 복원.</summary>
    public static void RestoreState(TreeView tv, ISet<string> expanded, string? selectedPath)
    {
        Walk(tv.Nodes);

        void Walk(TreeNodeCollection coll)
        {
            foreach (TreeNode n in coll)
            {
                string p = (string)n.Tag!;
                if (expanded.Contains(p)) n.Expand(); else n.Collapse();
                if (selectedPath is not null && p == selectedPath) tv.SelectedNode = n;
                Walk(n.Nodes);
            }
        }
    }

    private static void ApplyChecks(TreeNodeCollection coll, ISet<string> checkedPaths)
    {
        foreach (TreeNode n in coll)
        {
            if (checkedPaths.Contains((string)n.Tag!)) CheckRecursive(n, true);
            else ApplyChecks(n.Nodes, checkedPaths);
        }
    }
}
