# API contract (bản nháp)

> **Trạng thái:** bản nháp 03/11/2026 do Baro soạn từ Project Doc mục 5.5 và 6, để nhóm chốt trong buổi họp tuần 2.
> Đổi tên trường, đổi mã lỗi hay thêm endpoint thì phải báo cả nhóm và sửa file này trong cùng PR.

Hợp đồng này là ranh giới giữa Unity và AWS. Unity (client và game server) chỉ gọi các endpoint dưới đây qua HTTPS. Phía AWS (API Gateway HTTP API + Lambda + DynamoDB) chỉ cần trả đúng format. Hai bên làm song song được.

## 1. Quy ước chung

| Mục | Quy ước |
|---|---|
| Base URL | `https://<api-id>.execute-api.ap-southeast-1.amazonaws.com`. Không hard-code, không commit (client đọc từ `client-config.json`, server đọc biến môi trường `MM_API_URL`) |
| Định dạng | JSON, `Content-Type: application/json`, UTF-8 |
| Tên trường | camelCase; giá trị trạng thái viết HOA (`WAITING`, `FREE`...) |
| Thời gian | Chuỗi ISO 8601 UTC, ví dụ `2026-11-16T08:30:00Z` (riêng `ttl` trong DynamoDB là epoch giây, không lộ ra API) |
| ID | `playerId` = GUID client tự sinh và lưu trong PlayerPrefs · `ticketId` = `t-<uuid>` · `matchId` = `m-<uuid>` · `serverId` = `<instance-id>:<port>`, ví dụ `i-0abc123def:7777` |
| Không dùng `null` | Trường không có giá trị thì **bỏ hẳn** hoặc để chuỗi rỗng `""`. Unity `JsonUtility` không đọc được `null` hay kiểu nullable |
| Không dùng key động | `scores` là object với key cố định `p1`, `p2`. `JsonUtility` không đọc được Dictionary |

### Format lỗi (mọi endpoint)

```json
{ "error": { "code": "TICKET_NOT_FOUND", "message": "Ticket t-123 does not exist or has expired" } }
```

| HTTP | Khi nào | `code` ví dụ |
|---|---|---|
| 200 | Thành công | |
| 400 | Body hoặc query sai, thiếu trường | `INVALID_REQUEST` |
| 401 | Endpoint server thiếu hoặc sai khoá bí mật | `UNAUTHORIZED` |
| 404 | Không có ticket / server / match (kể cả ticket đã hết TTL) | `TICKET_NOT_FOUND`, `SERVER_NOT_FOUND` |
| 409 | Trạng thái không cho phép thao tác | `TICKET_ALREADY_MATCHED`, `MATCH_NOT_ON_SERVER` |
| 429 | API Gateway throttling | (do API Gateway trả, body có thể khác format trên) |
| 500 / 503 | Lỗi Lambda / DynamoDB | `INTERNAL` |

## 2. API cho client (người chơi)

Không cần xác thực. Chống spam bằng throttling của API Gateway.

### `POST /match`: bắt đầu tìm trận

```json
// request
{ "playerId": "3f6c2a9e-8b1d-4c7a-9e2f-1a2b3c4d5e6f" }
// 200
{ "ticketId": "t-123", "status": "WAITING" }
```

- **Idempotent theo `playerId`** (đề xuất): nếu người chơi đã có ticket `WAITING` thì trả lại chính ticket đó, không tạo ticket thứ hai. Nhờ vậy client retry an toàn khi mạng chập chờn.
- Lỗi: 400 nếu thiếu `playerId`.

### `GET /match?ticketId=t-123`: hỏi trạng thái (mỗi 1,5 giây)

```json
// đang chờ
{ "status": "WAITING" }
// đã ghép
{ "status": "MATCHED", "matchId": "m-456", "ip": "13.250.1.2", "port": 7777 }
// đã huỷ
{ "status": "CANCELLED" }
```

- 404 `TICKET_NOT_FOUND` nếu ticket không tồn tại hoặc đã bị TTL xoá; client coi như hết lượt và quay về menu.
- `ip` là IP **public** của EC2; `port` là cổng UDP của process server.

### `DELETE /match?ticketId=t-123`: huỷ tìm trận

```json
// 200
{ "status": "CANCELLED" }
```

- 409 `TICKET_ALREADY_MATCHED` nếu ticket vừa được ghép trước khi kịp huỷ. Body kèm `matchId`, `ip`, `port` để client tự quyết vào trận hay bỏ.
- Gọi lại với ticket đã `CANCELLED` vẫn trả 200 (idempotent).

### Hành vi phía client (Unity, tuần 4)

| Mục | Giá trị |
|---|---|
| Timeout mỗi request | 5 giây |
| Chu kỳ poll `GET /match` | 1,5 giây |
| Chờ tối đa | 60 giây, quá thì gọi `DELETE /match` và báo "Không tìm được trận" |
| Retry | Lỗi mạng, 429, 5xx: `GET` cứ poll tiếp; `POST` retry tối đa 2 lần (an toàn nhờ idempotent); không retry 4xx còn lại |
| Vào trận | Nhận `MATCHED` → kết nối UDP tới `ip:port`, gửi `matchId` + `playerId` trong ConnectionData của NGO |

## 3. API cho game server

Mọi request có header **`x-mm-server-key: <khoá bí mật>`**. Server nhận khoá qua biến môi trường `MM_SERVER_SECRET`, do User Data đọc từ Parameter Store (SecureString). HTTP API của API Gateway **không có API key như REST API**, nên khoá phải kiểm trong Lambda authorizer hoặc ngay trong Lambda; sai khoá thì trả 401.

### `POST /server/register`: khởi động xong

```json
// request
{ "serverId": "i-0abc123def:7777", "ip": "13.250.1.2", "port": 7777, "az": "ap-southeast-1a" }
// 200
{ "status": "FREE" }
```

- Idempotent: server khởi động lại (systemd restart) thì register lại cùng `serverId`, bản ghi về `FREE` và xoá `matchId` cũ.

### `POST /server/heartbeat`: mỗi 10 giây

```json
// request
{ "serverId": "i-0abc123def:7777", "status": "FREE" }
{ "serverId": "i-0abc123def:7777", "status": "BUSY", "matchId": "m-456" }
// 200, chưa được gán trận
{ "assignment": { "matchId": "" } }
// 200, đã được gán trận (Matchmaker đã chuyển server sang BUSY)
{ "assignment": { "matchId": "m-456", "p1": "3f6c2a9e-...", "p2": "8d1e7b4c-..." } }
```

- Cập nhật `lastHeartbeat`. Fleet Monitor đánh dấu `DEAD` khi `lastHeartbeat` cũ hơn 60 giây (mất khoảng 5 nhịp).
- `assignment` cho server biết trận nào sắp tới và 2 người chơi nào được phép vào (xem câu hỏi mở 1).
- 404 `SERVER_NOT_FOUND` nếu server chưa register (ví dụ bảng bị xoá): server gọi lại `register`.

### `POST /server/result`: hết trận hoặc huỷ trận

```json
// request
{
  "serverId": "i-0abc123def:7777",
  "matchId": "m-456",
  "players": { "p1": "3f6c2a9e-...", "p2": "8d1e7b4c-..." },
  "scores": { "p1": 87, "p2": 92 },
  "winner": "p2",
  "reason": "TimeUp",
  "startedAt": "2026-11-16T08:30:00Z",
  "endedAt": "2026-11-16T08:30:18Z"
}
// 200
{ "status": "FREE" }
```

- `winner`: `p1`, `p2`, `draw`, hoặc `none` khi trận bị huỷ.
- `reason`: `TimeUp` (hết giờ), `OpponentLeft` (một người thoát), `NoShow` (quá 30 giây mà chưa đủ 2 người vào).
- Ghi bảng Matches, đưa server về `FREE`. **Idempotent theo `matchId`**: gửi lại không ghi đè và vẫn trả 200, nên server được phép retry.
- 409 `MATCH_NOT_ON_SERVER` nếu `matchId` không được gán cho `serverId` này.

### Hành vi phía game server (Unity, tuần 5)

| Mục | Giá trị |
|---|---|
| Timeout mỗi request | 5 giây |
| `register` | Khi khởi động; lỗi thì retry mỗi 5 giây tới khi thành công (chưa register thì không nhận người chơi) |
| `heartbeat` | Mỗi 10 giây; lỗi thì bỏ qua, nhịp sau gửi tiếp |
| `result` | Retry backoff 1, 2, 4, 8, 16 giây vì mất kết quả là server kẹt ở `BUSY`; sau đó ghi log lỗi |
| Biến môi trường | `MM_API_URL`, `MM_SERVER_SECRET`, `MM_SERVER_ID`, `MM_PUBLIC_IP` (User Data lấy instance id, IP public qua IMDSv2 và khoá từ Parameter Store) |

## 4. Cấu hình client

`unity/Assets/StreamingAssets/client-config.json` (gitignored; commit bản `client-config.example.json`):

```json
{ "apiBaseUrl": "https://<api-id>.execute-api.ap-southeast-1.amazonaws.com" }
```

## 5. Câu hỏi mở cho buổi họp

| # | Câu hỏi | Đề xuất |
|---|---|---|
| 1 | Server biết trận được gán cho mình bằng cách nào, và chặn người lạ ra sao? | Heartbeat trả `assignment`. Client gửi `matchId` + `playerId` khi kết nối. Nếu server nhận yêu cầu kết nối với `matchId` chưa biết, nó tạm treo việc duyệt (NGO hỗ trợ duyệt bất đồng bộ, tối đa 10 giây) và gọi `heartbeat` ngay để lấy `assignment`, không chờ nhịp 10 giây kế tiếp. Nhờ vậy người chơi không phải đợi |
| 2 | `POST /server/result` có cần `serverId` và `players` không? (Project Doc 6.4 chưa có) | Có: Lambda cần `serverId` để đưa đúng server về `FREE`, và cần `players` để ghi bảng Matches |
| 3 | Người được ghép mà không vào trận | Server chờ 30 giây, quá hạn thì gửi `result` với `reason: NoShow`, `winner: none` |
| 4 | Một người chơi có được giữ nhiều ticket cùng lúc? | Không: `POST /match` idempotent theo `playerId` |
| 5 | Kiểm khoá bí mật của server ở đâu? | Lambda authorizer (dùng chung cho 3 endpoint `/server/*`) hoặc kiểm ngay trong Lambda; khoá lưu trong Parameter Store SecureString |
| 6 | Game server gọi API qua HTTP hay ghi thẳng DynamoDB? | Qua HTTP như Project Doc mục 6.3 (Unity không chứa AWS SDK hay access key) |
| 7 | Hết trận: server reset trong process hay thoát để systemd chạy lại? | Chưa chốt. Hiện server reset trong process và gửi `result` một lần cho mỗi trận |
| 8 | Ngưỡng Fleet Monitor đánh dấu `DEAD` | 60 giây không có heartbeat |
