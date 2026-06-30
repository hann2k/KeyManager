namespace KeyManager.Core;

/// <summary>
/// 콜론(:) 계층 키의 그룹 접근 규칙(와일드카드 없음).
/// 허용 이름 G 는 G 자신과 그 서브트리(G:로 시작)를 모두 허가한다. 세그먼트 경계로만 매칭.
/// </summary>
public static class KeyAccess
{
    public const char Separator = ':';

    /// <summary>요청 키가 허용 이름들 중 하나에 (자신 또는 하위로) 걸리는가.</summary>
    public static bool IsAllowed(IEnumerable<string> grants, string key)
    {
        foreach (string g in grants)
            if (Covers(g, key))
                return true;
        return false;
    }

    /// <summary>grant 가 key 를 포함하는가: key == grant 또는 key 가 grant + ":" 로 시작.</summary>
    public static bool Covers(string grant, string key)
        => string.Equals(key, grant, StringComparison.Ordinal)
           || key.StartsWith(grant + Separator, StringComparison.Ordinal);

    /// <summary>이름이 prefix 그룹(자신 또는 prefix: 하위)에 속하는가.</summary>
    public static bool InGroup(string prefix, string name)
        => string.Equals(name, prefix, StringComparison.Ordinal)
           || name.StartsWith(prefix + Separator, StringComparison.Ordinal);
}
