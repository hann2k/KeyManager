namespace KeyManager.App;

internal enum Lang { En, Ko }

/// <summary>
/// 경량 다국어 문자열 테이블. 기본 영어(En). 언어 변경 시 Changed 이벤트로 UI 갱신.
/// </summary>
internal static class Loc
{
    public static Lang Current { get; private set; } = Lang.En;

    /// <summary>언어가 바뀌면 발생.</summary>
    public static event Action? Changed;

    public static Lang Parse(string? code) =>
        string.Equals(code, "ko", StringComparison.OrdinalIgnoreCase) ? Lang.Ko : Lang.En;

    public static string Code(Lang l) => l == Lang.Ko ? "ko" : "en";

    public static void Set(Lang lang)
    {
        if (Current == lang) return;
        Current = lang;
        Changed?.Invoke();
    }

    /// <summary>초기 설정(이벤트 없이).</summary>
    public static void Init(Lang lang) => Current = lang;

    public static string T(string id)
    {
        var table = Current == Lang.Ko ? Ko : En;
        if (table.TryGetValue(id, out var s)) return s;
        return En.TryGetValue(id, out var e) ? e : id;
    }

    public static string T(string id, params object[] args) => string.Format(T(id), args);

    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        ["ok"] = "OK",
        ["cancel"] = "Cancel",
        ["save"] = "Save",
        ["close"] = "Close",
        ["app.title"] = "KeyManager",
        ["app.alreadyRunning"] = "KeyManager is already running.",
        ["err.fmt"] = "Error: {0}",

        ["tray.open"] = "Open",
        ["tray.lock"] = "Lock",
        ["tray.unlock"] = "Unlock",
        ["tray.exit"] = "Exit",
        ["tray.tipUnlocked"] = "KeyManager — unlocked",
        ["tray.tipLocked"] = "KeyManager — locked",

        ["mp.titleCreate"] = "Set master password",
        ["mp.titleUnlock"] = "Unlock",
        ["mp.infoCreate"] = "Creating a new vault. Choose a master password.\nIt cannot be recovered if lost.",
        ["mp.infoUnlock"] = "Enter the master password.",
        ["mp.password"] = "Password:",
        ["mp.confirm"] = "Confirm:",
        ["mp.errEmpty"] = "Enter a password.",
        ["mp.errMismatch"] = "Passwords do not match.",
        ["mp.errWrong"] = "Incorrect password.",

        ["tray.changePw"] = "Change master password",
        ["cpw.title"] = "Change master password",
        ["cpw.current"] = "Current password:",
        ["cpw.new"] = "New password:",
        ["cpw.confirm"] = "Confirm new password:",
        ["cpw.errEmpty"] = "Fill in all fields.",
        ["cpw.errMismatch"] = "The new passwords do not match.",
        ["cpw.errWrongCurrent"] = "The current password is incorrect.",
        ["cpw.success"] = "Master password changed. Consumer app seeds are unchanged.",
        ["cpw.reencrypted"] = "The master password is unchanged, but the vault was re-encrypted with a new salt (KDF upgrade). Consumer app seeds are unchanged.",

        ["sec.titleAdd"] = "Add key",
        ["sec.titleEdit"] = "Change value — {0}",
        ["sec.name"] = "Name:",
        ["sec.value"] = "Value:",
        ["sec.desc"] = "Description (optional):",
        ["sec.showValue"] = "Show value",
        ["sec.errNameValue"] = "Enter both name and value.",

        ["cli.titleRegister"] = "Register client",
        ["cli.titleEdit"] = "Edit permissions — {0}",
        ["cli.name"] = "Client name:",
        ["cli.allowed"] = "Allowed keys (check a group = whole subtree):",
        ["cli.register"] = "Register",
        ["cli.errName"] = "Enter a client name.",

        ["seed.title"] = "Seed for '{0}' (shown once)",
        ["seed.info"] = "Set this seed in the consumer app. You cannot view it again once this window closes.",
        ["seed.copy"] = "Copy to clipboard",
        ["seed.copied"] = "Copied!",

        ["tab.keys"] = "Keys",
        ["tab.clients"] = "Clients",
        ["tab.language"] = "Language",
        ["keys.legend"] = "●  = a key with a value (no marker = group)",

        ["btn.add"] = "Add",
        ["btn.changeValue"] = "Change value",
        ["btn.deleteKey"] = "Delete key",
        ["btn.deleteGroup"] = "Delete group",
        ["btn.description"] = "Description",
        ["btn.register"] = "Register",
        ["btn.editPerm"] = "Edit permissions",
        ["btn.delete"] = "Delete",

        ["msg.selectLeafChange"] = "Select a key (leaf) to change its value.",
        ["msg.selectLeafDelete"] = "Select a key (leaf) to delete.",
        ["msg.selectGroup"] = "Select a group (or key) to delete.",
        ["msg.selectNode"] = "Select an item to edit its description.",
        ["confirm.deleteKey"] = "Delete key '{0}'?",
        ["confirm.deleteGroup"] = "Delete all {1} keys under group '{0}'?\n(includes the whole subtree)",
        ["confirm.deleteClient"] = "Delete client '{0}'?",
        ["title.delete"] = "Delete",
        ["title.deleteGroup"] = "Delete group",

        ["desc.titleKey"] = "Description — {0}",
        ["desc.titleGroup"] = "Group description — {0}",
        ["desc.label"] = "Description:",

        ["lang.title"] = "Language / 언어",
        ["lang.info"] = "Select a language. / 언어를 선택하세요.",
        ["lang.english"] = "English",
        ["lang.korean"] = "한국어",
        ["langtab.info"] = "Selecting a language applies it immediately.",
    };

    private static readonly Dictionary<string, string> Ko = new(StringComparer.Ordinal)
    {
        ["ok"] = "확인",
        ["cancel"] = "취소",
        ["save"] = "저장",
        ["close"] = "닫기",
        ["app.title"] = "KeyManager",
        ["app.alreadyRunning"] = "KeyManager가 이미 실행 중입니다.",
        ["err.fmt"] = "오류: {0}",

        ["tray.open"] = "열기",
        ["tray.lock"] = "잠금",
        ["tray.unlock"] = "잠금 해제",
        ["tray.exit"] = "종료",
        ["tray.tipUnlocked"] = "KeyManager — 해제됨",
        ["tray.tipLocked"] = "KeyManager — 잠김",

        ["mp.titleCreate"] = "마스터 암호 설정",
        ["mp.titleUnlock"] = "잠금 해제",
        ["mp.infoCreate"] = "새 vault를 만듭니다. 마스터 암호를 정하세요.\n분실 시 복구할 수 없습니다.",
        ["mp.infoUnlock"] = "마스터 암호를 입력하세요.",
        ["mp.password"] = "암호:",
        ["mp.confirm"] = "확인:",
        ["mp.errEmpty"] = "암호를 입력하세요.",
        ["mp.errMismatch"] = "확인 암호가 일치하지 않습니다.",
        ["mp.errWrong"] = "암호가 올바르지 않습니다.",

        ["tray.changePw"] = "마스터 암호 변경",
        ["cpw.title"] = "마스터 암호 변경",
        ["cpw.current"] = "현재 암호:",
        ["cpw.new"] = "새 암호:",
        ["cpw.confirm"] = "새 암호 확인:",
        ["cpw.errEmpty"] = "모든 칸을 입력하세요.",
        ["cpw.errMismatch"] = "새 암호가 일치하지 않습니다.",
        ["cpw.errWrongCurrent"] = "현재 암호가 올바르지 않습니다.",
        ["cpw.success"] = "마스터 암호가 변경되었습니다. 소비 앱 시드는 그대로입니다.",
        ["cpw.reencrypted"] = "마스터 암호는 그대로지만, 내부 기밀이 새 salt로 재암호화되었습니다(KDF 업그레이드). 소비 앱 시드는 그대로입니다.",

        ["sec.titleAdd"] = "키 추가",
        ["sec.titleEdit"] = "값 변경 — {0}",
        ["sec.name"] = "이름:",
        ["sec.value"] = "값:",
        ["sec.desc"] = "설명 (선택):",
        ["sec.showValue"] = "값 표시",
        ["sec.errNameValue"] = "이름과 값을 모두 입력하세요.",

        ["cli.titleRegister"] = "클라이언트 등록",
        ["cli.titleEdit"] = "권한 편집 — {0}",
        ["cli.name"] = "클라이언트 이름:",
        ["cli.allowed"] = "접근 허용 키 (그룹 체크 = 하위 전체):",
        ["cli.register"] = "등록",
        ["cli.errName"] = "클라이언트 이름을 입력하세요.",

        ["seed.title"] = "'{0}' 시드 (1회만 표시)",
        ["seed.info"] = "아래 시드를 소비 앱에 설정하세요. 이 창을 닫으면 다시 볼 수 없습니다.",
        ["seed.copy"] = "클립보드로 복사",
        ["seed.copied"] = "복사됨!",

        ["tab.keys"] = "키",
        ["tab.clients"] = "클라이언트",
        ["tab.language"] = "언어",
        ["keys.legend"] = "●  = 값을 가진 키 (마커 없으면 그룹)",

        ["btn.add"] = "추가",
        ["btn.changeValue"] = "값 변경",
        ["btn.deleteKey"] = "키 삭제",
        ["btn.deleteGroup"] = "그룹 삭제",
        ["btn.description"] = "설명",
        ["btn.register"] = "등록",
        ["btn.editPerm"] = "권한 편집",
        ["btn.delete"] = "삭제",

        ["msg.selectLeafChange"] = "값을 변경할 키(잎 항목)를 선택하세요.",
        ["msg.selectLeafDelete"] = "삭제할 키(잎 항목)를 선택하세요.",
        ["msg.selectGroup"] = "삭제할 그룹(또는 키)을 선택하세요.",
        ["msg.selectNode"] = "설명을 편집할 항목을 선택하세요.",
        ["confirm.deleteKey"] = "'{0}' 키를 삭제할까요?",
        ["confirm.deleteGroup"] = "'{0}' 그룹의 {1}개 키를 모두 삭제할까요?\n(하위 전체 포함)",
        ["confirm.deleteClient"] = "'{0}' 클라이언트를 삭제할까요?",
        ["title.delete"] = "삭제",
        ["title.deleteGroup"] = "그룹 삭제",

        ["desc.titleKey"] = "설명 — {0}",
        ["desc.titleGroup"] = "그룹 설명 — {0}",
        ["desc.label"] = "설명:",

        ["lang.title"] = "Language / 언어",
        ["lang.info"] = "Select a language. / 언어를 선택하세요.",
        ["lang.english"] = "English",
        ["lang.korean"] = "한국어",
        ["langtab.info"] = "언어를 선택하면 즉시 적용됩니다.",
    };
}
