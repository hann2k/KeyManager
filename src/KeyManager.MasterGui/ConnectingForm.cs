using KeyManager.App;   // Loc (linked)
using KeyManager.Core;

namespace KeyManager.MasterGui;

/// <summary>
/// 서버 접속 진행 창(설계 §8). 먼저 "접속 중..."을 띄운 뒤 pull을 비동기로 수행한다.
/// - 성공: 자동으로 닫힘(<see cref="Succeeded"/>=true). <see cref="VaultJson"/>이 null이면 서버에 금고 없음.
/// - 실패: 라벨을 "서버 접속 불가"로 바꾸고 닫기 버튼을 노출(사용자가 닫으면 Succeeded=false).
/// UI 스레드를 블록하지 않아 창이 즉시 그려진다(과거엔 동기 블록으로 창 없이 얼어붙었음).
/// </summary>
internal sealed class ConnectingForm : Form
{
    private readonly MasterServerConnection _conn;
    private readonly Label _status;
    private readonly Button _closeButton;

    public bool Succeeded { get; private set; }
    public string? VaultJson { get; private set; }   // 성공 & null이면 서버가 비어 있음(첫 설정)

    public ConnectingForm(MasterServerConnection conn, string host, int port)
    {
        _conn = conn;

        Text = Loc.T("mg.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 140);
        ControlBox = false;   // 접속 중엔 닫기 불가(실패 시 버튼으로만)

        _status = new Label
        {
            Text = $"{Loc.T("mg.connecting")}\r\n{host}:{port}",
            AutoSize = false,
            Location = new Point(16, 18),
            Size = new Size(408, 70),
        };
        _closeButton = new Button
        {
            Text = Loc.T("close"),
            Location = new Point(340, 100),
            Size = new Size(84, 30),
            Visible = false,
        };
        _closeButton.Click += (_, _) => Close();

        Controls.Add(_status);
        Controls.Add(_closeButton);

        // 창이 보인 뒤 접속 시작(즉시 "접속 중" 노출).
        Shown += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            // 동기 블로킹이 섞여도 UI가 "접속 중"을 그리도록 스레드풀로 위임.
            string? json = await Task.Run(() => _conn.PullVaultAsync());
            Succeeded = true;
            VaultJson = json;
            DialogResult = DialogResult.OK;   // 성공 → 자동 닫힘
        }
        catch (MasterServerException ex)
        {
            // 접속 불가로 상태 전환 + 닫기 버튼 노출(창은 유지).
            _status.Text = Loc.T("mg.connectError", ex.Message);
            _closeButton.Visible = true;
            ControlBox = true;
        }
    }
}
