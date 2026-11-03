# fcaj-game-matchmaking

Project nhóm bootcamp FCAJ: **Multiplayer Matchmaking & Auto-scaling Game Server trên AWS**.
Game 2D 1v1 cố tình đơn giản (ai bấm Space nhiều hơn trong 15 giây thì thắng) để dồn sức cho phần AWS:
ghép trận bằng API Gateway + Lambda + DynamoDB, game server Unity Linux chạy trên EC2 trong Auto Scaling Group,
tự tăng/giảm số máy theo số server rảnh. Tự dựng bằng dịch vụ AWS cơ bản, không dùng GameLift. Region `ap-southeast-1`.

## Cấu trúc repo

| Thư mục | Nội dung |
|---|---|
| `unity/` | Project Unity (client + dedicated server dùng chung code) |
| `lambda/` | Matchmaker, Fleet Monitor, API cho game server (Node.js) |
| `bot/` | Script giả lập người chơi để test tải |
| `infra/` | CloudFormation / CDK |
| `docs/` | Sơ đồ kiến trúc `.drawio`, proposal, worklog, API contract, quyết định, tiến độ |
| `tools/` | Script build (Unity batchmode, đóng gói zip server) |

## Phiên bản bắt buộc

- **Unity 6000.3.10f1** (Unity 6.3 LTS), đúng bản này. Client và server phải build từ cùng commit và cùng bản Unity, lệch dễ lỗi kết nối.
- 2 module (Unity Hub → Installs → 6000.3.10f1 → Manage → Add modules):
  - Linux Build Support (Mono)
  - Linux Dedicated Server Build Support
- Netcode: Netcode for GameObjects (NGO) + Unity Transport (UDP), mô hình server-authoritative.
- Input System (mới), Multiplayer Play Mode để test nhiều người chơi trong Editor.

## Mở project Unity

1. Clone repo: `git clone https://github.com/Baro-Duong/fcaj-game-matchmaking.git`
2. Unity Hub → Projects → Add → Add project from disk → chọn thư mục `unity/` (không chọn gốc repo).
3. Lần mở đầu Unity tạo lại `Library/` nên sẽ lâu vài phút.

## Test local (Host + Client trong Editor)

1. Mở scene `Assets/_Project/Scenes/Main.unity`.
2. Window → Multiplayer → Multiplayer Play Mode → bật Player 2.
3. Bấm Play. Cửa sổ chính bấm **Host**, cửa sổ Player 2 bấm **Client** (mặc định `127.0.0.1:7777`).
4. Kết quả mong đợi: 2 ô vuông, bấm Space thì điểm tăng ở cả hai bên, đếm ngược 3-2-1, hết giờ hiện người thắng rồi tự reset.

## Git workflow

1. `main` luôn chạy được và được bảo vệ: không push thẳng, chỉ merge qua Pull Request.
2. Kéo `main` mới nhất → tạo nhánh `feature/<tên-việc>` (ví dụ `feature/matchmaker-lambda`).
3. Commit nhỏ, message tiếng Anh: `feat: …`, `fix: …`, `chore: …`, `docs: …`.
4. Mở PR vào `main`, có mục **How to test**. Ít nhất 1 người khác review rồi mới merge.

Quy ước Unity:
- Visible Meta Files + Force Text; luôn commit file `.meta` cùng asset.
- Một scene chỉ một người sửa tại một thời điểm (báo nhóm trước khi sửa `Main.unity`); chia việc theo prefab/script.
- Cài package thì commit `Packages/manifest.json` + `Packages/packages-lock.json`.
- Không commit `unity/Builds/` (bản build server đưa lên S3).

## Bí mật

Không bao giờ commit: key `.pem`/`.ppk`, file `.env`, access key, URL API / Function URL, Account ID.
Ảnh chụp màn hình đưa vào repo phải che Account ID, IP public, email.

## Tài liệu

- [docs/decisions.md](docs/decisions.md): các quyết định đã chốt và câu hỏi còn mở
- [docs/progress.md](docs/progress.md): tiến độ theo tuần
- `docs/api-contract.md`: hợp đồng API giữa Unity và AWS (đang soạn)
- `docs/server-runbook.md`: cách chạy game server Linux (đang soạn)
- `docs/fcaj-multiplayer-matchmaking.drawio`: sơ đồ kiến trúc
