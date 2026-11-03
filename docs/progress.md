# Tiến độ

Ký hiệu: ✅ xong · ⏳ đang làm · ⬜ chưa bắt đầu · ❌ lỗi / bị chặn

## Tuần 2 (02–08/11): nền móng

| | Việc | Người | Ghi chú |
|---|---|---|---|
| ⬜ | Chốt đề tài, phân công, tài khoản AWS dùng chung | Cả nhóm | |
| ⏳ | Tạo repo GitHub, mời thành viên, bảo vệ `main` | Baro | Repo đã có trên GitHub (1 commit); chưa mời, chưa bảo vệ `main` |
| ⬜ | Game 2D bấm Space chạy local (Host + Client) | Baro | |
| ⬜ | Build được bản Linux Dedicated Server | Baro | |
| ⬜ | Chốt hợp đồng API (Project Doc mục 6) | Cả nhóm | Baro soạn bản nháp `docs/api-contract.md` |

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
- ⬜ 1.9 Từ đây làm trên nhánh `feature/*` + PR

**Bước 2: game 2D bấm Space chạy local** (`feature/space-game-local`)
- ✅ Script: GameConfig, CommandLineArgs, NetworkBootstrap, MatchManager, PlayerPresser, PressRateLimiter, ConnectionPanel, MatchHud; compile sạch (03/11)
- ✅ Scene `Main.unity` + prefab Player dựng bằng Editor script `MM/Setup/Build Main Scene`; đã kiểm tra YAML: PlayerPrefab, prefab list, approval, tick 30, port 7777, mọi tham chiếu UI (03/11)
- ✅ Test Multiplayer Play Mode: Host + Client, người thứ 3 bị từ chối (`full`), thoát giữa trận (`OpponentLeft`), chế độ Server, giới hạn tốc độ bấm (max 3 → P2 bị bỏ 48 lần bấm) (03/11)
- ✅ Build Windows Client (`SpaceDuel.exe`): 2 client .exe chơi với Server trong Editor (03/11)
- ⏳ PR vào `main` có mục How to test

**Bước 3: build Linux Dedicated Server** (`feature/linux-server-build`)
- ⬜ Cài 2 module Linux trong Unity Hub
- ⬜ `BuildScript.cs` (menu MM/Build + `-executeMethod`), `build-info.txt`
- ⬜ `tools/build-server.ps1` + zip `server-<version>.zip`
- ⬜ Smoke test chạy server trên Linux (dòng `[MM] Server listening`, UDP 7777 mở)
- ⬜ `docs/server-runbook.md`

**Bước 4: bản nháp API contract**
- ⬜ `docs/api-contract.md` để nhóm chốt trong buổi họp tuần

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
