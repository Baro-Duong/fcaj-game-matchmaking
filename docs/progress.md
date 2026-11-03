# Tiến độ

Ký hiệu: ✅ xong · ⏳ đang làm · ⬜ chưa bắt đầu · ❌ lỗi / bị chặn

## Tuần 2 (02–08/11): nền móng

| | Việc | Người | Ghi chú |
|---|---|---|---|
| ⬜ | Chốt đề tài, phân công, tài khoản AWS dùng chung | Cả nhóm | |
| ⏳ | Tạo repo GitHub, mời thành viên, bảo vệ `main` | Baro | Repo đã có trên GitHub (1 commit); chưa mời, chưa bảo vệ `main` |
| ✅ | Game 2D bấm Space chạy local (Host + Client) | Baro | PR #1 đã merge (03/11) |
| ✅ | Build được bản Linux Dedicated Server | Baro | PR #2 đã merge (03/11); chạy trên Ubuntu 24.04, không chạy trên Amazon Linux 2023 |
| ⏳ | Chốt hợp đồng API (Project Doc mục 6) | Cả nhóm | Bản nháp `docs/api-contract.md` xong (03/11), chờ họp nhóm chốt 8 câu hỏi mở |

### Mảng Unity: chi tiết

**Bước 1: repo + project Unity 2D**
- ✅ 1.1 Khảo sát folder + git (03/11)
- ✅ 1.2 Khung repo: `.gitignore`, `.gitattributes`, `README.md`, README từng thư mục, `docs/decisions.md`, `docs/progress.md` (03/11)
- ✅ 1.3 Project Unity 2D (Universal 2D, 6000.3.10f1) trong `unity/`; project 3D cũ chuyển ra `Study2/old-3d-test/`; Player Settings: SpaceDuel, Windowed 960×540, Resizable, Run In Background (03/11). Company Name còn để `DefaultCompany`
- ✅ 1.4 Netcode for GameObjects 2.13.3 (kéo theo Unity Transport 2.6.0) + Multiplayer Play Mode 2.0.2 (03/11)
- ✅ 1.5 Kiểm tra bằng file: ProjectVersion, manifest, packages-lock, Force Text, Visible Meta Files, Input System, Run In Background; Editor.log không có lỗi (03/11)
- ✅ 1.6 `git status`: 60 file, không có Library/Temp/Logs/UserSettings/.sln/file bí mật, asset nào cũng có .meta (03/11)
- ✅ 1.7 Commit đầu `349ead4` đã push lên `main`; xoá nhánh `switch-2d` (03/11)
- ⏳ 1.8 Mời thành viên + bảo vệ `main`
- ✅ 1.9 Từ đây làm trên nhánh `feature/*` + PR (PR #1 là PR đầu tiên)

**Bước 2: game 2D bấm Space chạy local** (`feature/space-game-local`)
- ✅ Script: GameConfig, CommandLineArgs, NetworkBootstrap, MatchManager, PlayerPresser, PressRateLimiter, ConnectionPanel, MatchHud; compile sạch (03/11)
- ✅ Scene `Main.unity` + prefab Player dựng bằng Editor script `MM/Setup/Build Main Scene`; đã kiểm tra YAML: PlayerPrefab, prefab list, approval, tick 30, port 7777, mọi tham chiếu UI (03/11)
- ✅ Test Multiplayer Play Mode: Host + Client, người thứ 3 bị từ chối (`full`), thoát giữa trận (`OpponentLeft`), chế độ Server, giới hạn tốc độ bấm (max 3 → P2 bị bỏ 48 lần bấm) (03/11)
- ✅ Build Windows Client (`SpaceDuel.exe`): 2 client .exe chơi với Server trong Editor (03/11)
- ✅ PR #1 vào `main` có mục How to test, đã merge `f3e52f4` (03/11)

**Bước 3: build Linux Dedicated Server** (`feature/linux-server-build`)
- ✅ 2 module Linux (Mono + Dedicated Server) đã có sẵn trong 6000.3.10f1 (03/11)
- ✅ `tools/build-server.ps1` + zip `server-1.0.zip` (31 MB, đường dẫn `/`, không có `_DoNotShip`); server giải nén từ zip chạy được trên Ubuntu 24.04 (03/11)
- ✅ Sửa lỗi build batch mode để Editor kẹt ở Linux Server: giờ luôn chuyển về Windows Player sau khi build (03/11)
- ✅ Smoke test server Linux bằng Docker (03/11): ❌ Amazon Linux 2023 lỗi `GLIBC_2.35 not found`; ✅ Ubuntu 24.04 nghe UDP 7777, 2 `Client.exe` chơi trọn trận, CPU ~1%, RAM ~105 MB. AMI chờ nhóm chốt (`decisions.md`)
- ✅ `BuildScript.cs`: build Linux Server + Windows Client OK, build lỗi không mất bản cũ, chạy đang mở thì từ chối (03/11)
- ✅ `docs/server-runbook.md`: lệnh chạy, tham số, log, mã thoát, systemd mẫu, yêu cầu Ubuntu ≥ 22.04 (03/11)
- ✅ PR #2 đã merge `ea528d4` (03/11)

**Bước 4: bản nháp API contract**
- ✅ `docs/api-contract.md` bản nháp: quy ước chung, format lỗi, 6 endpoint kèm JSON mẫu, timeout/retry, cấu hình, 8 câu hỏi mở (03/11)
- ⬜ Họp nhóm chốt các câu hỏi mở, ghi kết quả vào `decisions.md` và sửa Project Doc mục 6 / 7.2

## Tuần 3 (09–15/11): 1 server trên 1 EC2, làm tay
- ⬜ Client nhập IP public:port; test 2 client từ 2 mạng khác nhau
- ⬜ Hướng dẫn chạy server trên EC2 (S3 + IAM Role, systemd, Session Manager)

## Tuần 4 (16–22/11): ghép trận
- ⬜ MatchmakingClient: POST/GET/DELETE `/match`, timeout, thông báo lỗi

## Tuần 5 (23–29/11): vòng đời server (mốc quyết định)
- ⬜ ServerBackendClient: register → heartbeat → result

## Tuần 6–9
- ⬜ T6 (30/11–06/12): ASG + Fleet Monitor
- ⬜ T7 (07–13/12): auto scaling + bot
- ⬜ T8 (14–20/12): IaC + báo cáo chi phí
- ⬜ T9 (21–27/12): video demo + nộp
- Dự phòng 28–31/12
