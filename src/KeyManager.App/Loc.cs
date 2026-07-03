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
        ["warn.master"] = "⚠ Keep your master password in your memory only — never write it down or share it. If lost or leaked, it cannot be recovered; you bear sole responsibility and the developer is not liable.",
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

        // ---- TCP: Server tray ----
        ["srv.title"] = "KeyManager Server",
        ["srv.menu.keys"] = "View key list",
        ["srv.menu.exit"] = "Exit",
        ["srv.tipListening"] = "KeyManager Server — listening on port {0}",
        ["srv.startError"] = "Failed to start the server: {0}",
        ["srv.alreadyRunning"] = "KeyManager Server is already running.",
        ["srv.status.running"] = "KeyManager Server — running (port {0})",
        ["srv.status.stopped"] = "KeyManager Server — stopped",
        ["srv.tray.serverExeMissing"] = "Server executable not found next to the tray app:\n{0}\nStart the server separately, or reinstall so both files are in the same folder.",
        ["srv.list.notRunning"] = "The server is not running yet (no data).",

        // ---- TCP: Admin token one-time reveal ----
        ["srv.token.title"] = "Admin token (shown once)",
        ["srv.token.info"] = "The server is now listening. Enter the admin token below in the Master GUI the first time you connect.\nThis token is shown only once. You cannot view it again once this window closes.",
        ["srv.token.port"] = "Listening port:",
        ["srv.token.label"] = "Admin token:",
        ["srv.token.copy"] = "Copy to clipboard",
        ["srv.token.copied"] = "Copied!",

        // ---- TCP: Server read-only metadata window ----
        ["srv.list.title"] = "Registered clients (read-only)",
        ["srv.list.note"] = "The server has no master password, so it cannot read key names or values. Only client (envelope) names and their last update time are shown.",
        ["srv.list.vaultPresent"] = "Master vault: stored",
        ["srv.list.vaultAbsent"] = "Master vault: not stored yet",
        ["srv.list.envCount"] = "Registered clients (envelopes): {0}",
        ["srv.list.colClient"] = "Client",
        ["srv.list.colUpdated"] = "Last updated",
        ["srv.list.empty"] = "No clients registered yet.",
        ["srv.list.refresh"] = "Refresh",

        // ---- TCP: Master GUI connection setup ----
        ["mg.setup.title"] = "Connect to server",
        ["mg.setup.info"] = "Enter the KeyManager server address and the admin token shown once on the server's first run.",
        ["mg.setup.host"] = "Host:",
        ["mg.setup.port"] = "Port:",
        ["mg.setup.token"] = "Admin token:",
        ["mg.setup.errHost"] = "Enter a host.",
        ["mg.setup.errPort"] = "Enter a valid port (1-65535).",
        ["mg.setup.errToken"] = "Paste a valid admin token (base64).",

        // ---- TCP: Master GUI flow / push status ----
        ["mg.title"] = "KeyManager Master",
        ["mg.connecting"] = "Connecting to the server...",
        ["mg.connectError"] = "Cannot reach the server: {0}",
        ["mg.firstSetup"] = "The server has no vault yet. Create a master password to set one up.",
        ["mg.pushError"] = "Failed to sync to the server: {0}\nYour changes are kept locally and will be retried on the next edit or on close.",
        ["mg.closePushError"] = "Some changes could not be synced to the server before closing: {0}",
        ["mg.importPrompt"] = "An existing local vault (stage-1 Named Pipe app) was found:\n{0}\n\nImport it to this server? You will unlock it with its existing master password.\n(The original file is left untouched.)",
        ["mg.importDone"] = "The existing vault was imported to the server.",
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
        ["warn.master"] = "⚠ 마스터 암호는 본인만 기억하세요 — 어디에도 기록하거나 공유하지 마세요. 분실·유출 시 복구가 불가능하며, 그 책임은 전적으로 사용자에게 있고 개발사는 책임지지 않습니다.",
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

        // ---- TCP: 서버 트레이 ----
        ["srv.title"] = "KeyManager 서버",
        ["srv.menu.keys"] = "Key 목록 보기",
        ["srv.menu.exit"] = "종료",
        ["srv.tipListening"] = "KeyManager 서버 — 포트 {0} 수신 중",
        ["srv.startError"] = "서버를 시작하지 못했습니다: {0}",
        ["srv.alreadyRunning"] = "KeyManager 서버가 이미 실행 중입니다.",
        ["srv.status.running"] = "KeyManager 서버 — 실행 중 (포트 {0})",
        ["srv.status.stopped"] = "KeyManager 서버 — 중지됨",
        ["srv.tray.serverExeMissing"] = "트레이 앱 옆에서 서버 실행 파일을 찾을 수 없습니다:\n{0}\n서버를 따로 실행하거나, 두 파일이 같은 폴더에 있도록 다시 설치하세요.",
        ["srv.list.notRunning"] = "서버가 아직 실행되지 않았습니다 (데이터 없음).",

        // ---- TCP: admin 토큰 1회 표시 ----
        ["srv.token.title"] = "admin 토큰 (1회만 표시)",
        ["srv.token.info"] = "서버가 수신을 시작했습니다. 마스터 GUI에 처음 접속할 때 아래 admin 토큰을 입력하세요.\n이 토큰은 이번 한 번만 표시됩니다. 이 창을 닫으면 다시 볼 수 없습니다.",
        ["srv.token.port"] = "수신 포트:",
        ["srv.token.label"] = "admin 토큰:",
        ["srv.token.copy"] = "클립보드로 복사",
        ["srv.token.copied"] = "복사됨!",

        // ---- TCP: 서버 읽기전용 메타데이터 창 ----
        ["srv.list.title"] = "등록된 클라이언트 (읽기 전용)",
        ["srv.list.note"] = "서버에는 마스터 암호가 없어 키 이름이나 값을 볼 수 없습니다. 클라이언트(봉투) 이름과 마지막 갱신 시각만 표시됩니다.",
        ["srv.list.vaultPresent"] = "마스터 금고: 보관됨",
        ["srv.list.vaultAbsent"] = "마스터 금고: 아직 없음",
        ["srv.list.envCount"] = "등록된 클라이언트(봉투): {0}개",
        ["srv.list.colClient"] = "클라이언트",
        ["srv.list.colUpdated"] = "마지막 갱신",
        ["srv.list.empty"] = "아직 등록된 클라이언트가 없습니다.",
        ["srv.list.refresh"] = "새로 고침",

        // ---- TCP: 마스터 GUI 접속 설정 ----
        ["mg.setup.title"] = "서버 접속",
        ["mg.setup.info"] = "KeyManager 서버 주소와, 서버 최초 실행 시 1회 표시된 admin 토큰을 입력하세요.",
        ["mg.setup.host"] = "호스트:",
        ["mg.setup.port"] = "포트:",
        ["mg.setup.token"] = "admin 토큰:",
        ["mg.setup.errHost"] = "호스트를 입력하세요.",
        ["mg.setup.errPort"] = "올바른 포트(1-65535)를 입력하세요.",
        ["mg.setup.errToken"] = "올바른 admin 토큰(base64)을 붙여넣으세요.",

        // ---- TCP: 마스터 GUI 흐름 / push 상태 ----
        ["mg.title"] = "KeyManager 마스터",
        ["mg.connecting"] = "서버에 접속 중...",
        ["mg.connectError"] = "서버에 연결할 수 없습니다: {0}",
        ["mg.firstSetup"] = "서버에 아직 금고가 없습니다. 마스터 암호를 생성해 초기 설정을 진행하세요.",
        ["mg.pushError"] = "서버 동기화에 실패했습니다: {0}\n변경 내용은 로컬에 유지되며, 다음 편집 또는 종료 시 다시 시도합니다.",
        ["mg.closePushError"] = "종료 전 일부 변경 내용을 서버에 동기화하지 못했습니다: {0}",
        ["mg.importPrompt"] = "기존 로컬 금고(1단계 Named Pipe 앱)를 발견했습니다:\n{0}\n\n이 금고를 서버로 가져올까요? 기존 마스터 암호로 잠금을 해제합니다.\n(원본 파일은 그대로 보존됩니다.)",
        ["mg.importDone"] = "기존 금고를 서버로 가져왔습니다.",
    };
}
