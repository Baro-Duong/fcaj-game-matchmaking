# Quyết định

Ghi mọi quyết định của nhóm theo thứ tự mới nhất ở trên. Đổi quyết định thì thêm dòng mới, không xoá dòng cũ.

## Đã chốt

| Ngày | Quyết định | Lý do |
|---|---|---|
| 03/11/2026 | Repo dạng monorepo theo Project Doc mục 10.1; project Unity nằm trong `unity/`, `.gitignore` + `.gitattributes` ở gốc repo; thêm `tools/` cho script build | Một repo cho cả Unity, Lambda, IaC; đường dẫn Unity cố định để script build và User Data không phải đổi |
| 28/10/2026 | Game 2D 1v1: ai bấm Space nhiều hơn trong thời gian quy định thì thắng | Chấm điểm không tính gameplay; game đơn giản để dồn sức cho AWS |
| 28/10/2026 | Phương án dự phòng: lùi về "Một EC2 server" nếu hết tuần 5 chưa xong matchmaking | Giảm rủi ro trễ hạn 31/12 |
| 28/10/2026 | Yêu cầu FCAJ: proposal nộp chung với workshop, không giới hạn dịch vụ, tối ưu chi phí được điểm cao | Tiêu chí chấm của bootcamp |
| 27/10/2026 | Tự dựng bằng dịch vụ AWS cơ bản, không dùng GameLift | Project chấm kiến trúc AWS; GameLift làm hộ đúng phần cần thể hiện |
| 27/10/2026 | Unity 6000.3.10f1 + Netcode for GameObjects + Unity Transport | Cả nhóm dùng đúng một bản để client và server không lệch phiên bản |
| 27/10/2026 | Region `ap-southeast-1` (Singapore) | Gần Việt Nam, độ trễ thấp |
| 27/10/2026 | Kiểm chứng tài khoản: Launch Template + Auto Scaling Group chạy 3 máy trên 2 AZ, tự thay máy hỏng (đã dọn) | Xác nhận tài khoản Free plan đủ quyền cho kiến trúc dự kiến |

## Câu hỏi mở (mặc định đang dùng cho tới khi nhóm chốt)

| Câu hỏi | Mặc định đang dùng |
|---|---|
| Thời lượng một trận | 15 giây (config + tham số `-matchSeconds`) |
| Ngưỡng chống macro | 20 lần bấm/giây/người (config + tham số `-maxPps`) |
| Người chơi thoát giữa trận | Kết thúc trận ngay, người còn lại thắng, lý do `opponent_left` |
| Được ghép mà không vào trận | Server chờ 30 giây, quá hạn thì huỷ trận, về FREE |
| Hết trận: reset trong process hay thoát để systemd chạy lại | Local/Editor: reset trong process; server trên EC2: chưa chốt |
| Server biết trận nào gán cho mình và chặn người lạ thế nào | Đề xuất: response của heartbeat trả `matchId` + 2 `playerId`; client gửi `matchId` + `playerId` trong ConnectionData để server duyệt |
| 1 hay nhiều process server trên 1 EC2 | Code hỗ trợ `-port`; Security Group hiện chỉ mở UDP 7777 |
| Game server gọi API qua HTTP hay ghi thẳng DynamoDB | Qua HTTP (giữ Unity đơn giản, không chứa AWS SDK/key) |
| Client chờ ghép trận tối đa bao lâu | 60 giây rồi báo "không tìm được trận" |
| AMI cho game server (Project Doc mục 7.2 ghi Amazon Linux) | Đề xuất Ubuntu Server 24.04 LTS x86_64. Smoke test 03/11 bằng Docker: Amazon Linux 2023 lỗi `GLIBC_2.35 not found` (chỉ có 2.34); Ubuntu 24.04 chạy trọn trận với 2 client. Cần nhóm chốt và sửa Project Doc mục 7.2 |
