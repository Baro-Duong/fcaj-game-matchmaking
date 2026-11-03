# Game server runbook

Cách chạy game server Linux cho người làm AWS. Không cần biết Unity: server là một file chạy được, nhận tham số dòng lệnh, nghe một cổng UDP và ghi log ra stdout.

## Tóm tắt

| Mục | Giá trị |
|---|---|
| File chạy | `Server.x86_64` (Unity Linux Dedicated Server, headless, không cần GPU hay màn hình) |
| Hệ điều hành | Linux **x86_64** có **glibc ≥ 2.35**: Ubuntu 22.04 / 24.04 LTS. **Amazon Linux 2023 không chạy được** (glibc 2.34, xem bên dưới). t3.micro khi test, t3.small nếu cần |
| Cổng | **UDP 7777** (đổi bằng `-port`) |
| Người chơi | 2 người một trận; người thứ 3 bị từ chối với lý do `full` |
| CPU | Giới hạn 30 khung hình/giây để không đốt CPU credit của dòng t3 |
| Log | stdout khi chạy với `-logFile -`; dòng của game có tiền tố `[MM]` |
| Gói build | `server-<version>.zip` trên S3: `builds/server-<version>.zip` |

Graviton (ARM, ví dụ t4g) **không chạy được** bản này. Muốn dùng ARM thì phía Unity phải build thêm bản Linux ARM64 (IL2CPP).

**Amazon Linux 2023 không chạy được** (smoke test 03/11 trong container `amazonlinux:2023`, bản 2023.12.20261102):

```
/opt/spaceduel/Server.x86_64: /lib64/libm.so.6: version `GLIBC_2.35' not found (required by /opt/spaceduel/UnityPlayer.so)
```

`UnityPlayer.so` của Unity 6000.3 cần glibc 2.35, AL2023 cố định glibc 2.34 suốt vòng đời nên cập nhật cũng không hết. AMI Ubuntu trên EC2 miễn phí như Amazon Linux (giá máy không đổi) và có sẵn SSM Agent.

**Đã kiểm chứng trên Ubuntu 24.04.5 LTS** (glibc 2.39, container `ubuntu:24.04`, 03/11): server khởi động, nghe UDP 7777, 2 client Windows chơi trọn một trận, tự bắt đầu trận mới, xử lý client thoát; không có exception. Lúc chạy: CPU khoảng 1%, RAM khoảng 105 MB.

Chạy thử lại trên máy có Docker (không tốn AWS):

```bash
docker run --rm -p 7777:7777/udp -v "<đường dẫn>/unity/Builds/LinuxServer:/build:ro" ubuntu:24.04 \
  bash -c "cp -r /build /opt/spaceduel && chmod +x /opt/spaceduel/Server.x86_64 && exec /opt/spaceduel/Server.x86_64 -port 7777 -logFile -"
```

Rồi mở 2 client, kết nối `127.0.0.1:7777`. Image Docker tối giản thiếu chứng chỉ CA nên log có `Curl error 35: Cert handshake failed` (Unity tự gọi HTTPS lúc khởi động); AMI Ubuntu có sẵn gói `ca-certificates` nên không gặp. Server cần gói này cho các lệnh gọi API qua HTTPS từ tuần 5.

## Trong file zip

| File / thư mục | Vai trò |
|---|---|
| `Server.x86_64` | File chạy |
| `Server_Data/` | Dữ liệu game, thư viện managed (bắt buộc đi kèm, cùng thư mục) |
| `UnityPlayer.so` | Runtime Unity (bắt buộc) |
| `libdecor-0.so.0`, `libdecor-cairo.so` | Thư viện Unity tự đóng kèm, giữ nguyên |
| `build-info.txt` | `version`, `commit`, `built_at_utc`… để biết đang chạy bản nào |

Giải nén giữ nguyên cấu trúc; chỉ chạy được khi cả bộ nằm cùng một thư mục. Bản build 03/11 nặng khoảng 88 MB khi giải nén (`Server_Data` 50 MB, `UnityPlayer.so` 37 MB).

## Chạy

```bash
sudo apt-get install -y unzip               # Ubuntu AMI có thể chưa có unzip
mkdir -p /opt/spaceduel
unzip -o server-1.0.zip -d /opt/spaceduel
chmod +x /opt/spaceduel/Server.x86_64      # zip tạo trên Windows làm mất quyền thực thi
/opt/spaceduel/Server.x86_64 -port 7777 -logFile -
```

Khởi động thành công khi thấy:

```
[MM] Match config: 15s, max 20 presses/s
[MM] Server listening on 0.0.0.0:7777
```

## Tham số

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `-port <n>` | `7777` | Cổng UDP. Chạy nhiều process trên 1 máy thì mỗi process một cổng (dải đề xuất 7777–7786) |
| `-matchSeconds <s>` | `15` | Thời lượng một trận (giây) |
| `-maxPps <n>` | `20` | Số lần bấm tối đa mỗi giây mỗi người (chống macro) |
| `-logFile -` | (file mặc định của Unity) | Ghi log ra stdout, để systemd/journald hoặc CloudWatch Agent thu |

`-batchmode -nographics` thêm vào cũng được nhưng không cần: bản Dedicated Server vốn đã headless. `-server` chỉ dùng cho bản client muốn chạy làm server, bản này không cần.

## Log cần biết

| Dòng log | Nghĩa |
|---|---|
| `[MM] Server listening on 0.0.0.0:7777` | Sẵn sàng nhận người chơi |
| `[MM] Client 1 connected` / `disconnected` | Người chơi vào / ra |
| `[MM] Rejected client 3: full` | Đã đủ 2 người |
| `[MM] Rejected client 3: match in progress` | Trận đang diễn ra |
| `[MM] Match started (15s)` | Bắt đầu trận |
| `[MM] Match ended winner=p2 reason=TimeUp scores=p1:87 p2:92 ...` | Kết quả (`reason` = `TimeUp` hoặc `OpponentLeft`; `winner` = `p1`, `p2` hoặc `draw`) |
| `[MM] Rate limit: client 1 (P2) ...` | Có người bấm vượt ngưỡng (nghi macro) |
| `[MM] Failed to start server ... (port in use?)` | Không mở được cổng; process thoát với mã 1 |

## Mã thoát và cách dừng

| Mã | Khi nào |
|---|---|
| `0` | Dừng bình thường |
| `1` | Không khởi động được (cổng bận, lỗi transport) |

Dừng bằng SIGTERM (`kill <pid>` hoặc `systemctl stop`). Hiện server chạy liên tục, hết trận thì tự reset và chờ trận mới; chưa tự thoát sau mỗi trận (câu hỏi mở trong `decisions.md`).

## systemd (bản nháp cho tuần 3)

```ini
# /etc/systemd/system/spaceduel.service
[Unit]
Description=Space Duel game server (UDP 7777)
After=network-online.target
Wants=network-online.target

[Service]
WorkingDirectory=/opt/spaceduel
ExecStart=/opt/spaceduel/Server.x86_64 -port 7777 -logFile -
Restart=always
RestartSec=2
User=ubuntu

[Install]
WantedBy=multi-user.target
```

Xem log: `journalctl -u spaceduel -f`.

## Yêu cầu mạng

- Security Group: inbound **UDP 7777** (hoặc dải cổng nếu chạy nhiều process); không mở TCP 22, vào máy bằng Session Manager.
- Server tự nghe trên `0.0.0.0`; client kết nối bằng IP public của EC2.
- Từ tuần 5 server sẽ gọi API `/server/register`, `/server/heartbeat`, `/server/result` qua HTTPS (outbound 443), nhận cấu hình qua biến môi trường (`MM_API_URL`, `MM_SERVER_SECRET`, `MM_SERVER_ID`, `MM_PUBLIC_IP`). Chưa có trong bản này.

## Build lại (phía Unity)

Client và server phải build từ **cùng commit** và **cùng bản Unity 6000.3.10f1**, lệch thì dễ lỗi kết nối.

- Trong Editor: menu **MM → Build → Linux Server** → `unity/Builds/LinuxServer/`.
- Không mở Editor (đóng Unity trước): `powershell -ExecutionPolicy Bypass -File tools\build-server.ps1` → `unity/Builds/server-<version>.zip`.
- Mỗi lần đưa lên S3 thì tăng **Player Settings → Version** để tên zip không đè bản cũ.
