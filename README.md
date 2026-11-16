# TrackLab API (backend .NET)

Backend untuk **TrackLab**, aplikasi web untuk melacak request analisis lab. Backend ini berada di antara frontend Next.js dan API assessment, dan menambahkan hal-hal yang diminta brief untuk sisi backend:

- **Auth gateway**: login, logout, data user yang sedang login, dan refresh token otomatis. Token disimpan di cookie HttpOnly terenkripsi dan tidak pernah sampai ke browser.
- **CRUD gateway** untuk Master Trackers, Transaction Requests, dan Transaction Trackings, dengan validasi input.
- **Logging** (fitur 4): log JSON terstruktur dengan correlation ID per request, endpoint untuk menampung error dari frontend, dan endpoint untuk membaca log.
- **Audit trail** (bonus BE): setiap create, update, dan delete dicatat beserta user, waktu, dan perubahan per field.

Repositori frontend: lihat `tracklab-frontend` (Next.js).

---

## Arsitektur

```
Browser ──► Next.js (localhost:3000) ──/api/*──► TrackLab.Api (localhost:5080) ──► API assessment
                                                 │                                (api-assessment-biopharma.kalbe.co.id)
                                                 ├─ logs/tracklab-YYYYMMDD.jsonl   (log terstruktur)
                                                 └─ data/audit-trail.jsonl         (audit trail)
```

- Browser hanya berkomunikasi dengan aplikasi Next.js. Next.js meneruskan `/api/*` ke backend ini, sehingga cookie sesi bersifat first-party dan tidak perlu konfigurasi CORS.
- Backend ini menambahkan `app_id` / `app_key` ke setiap panggilan ke API assessment. Rahasia tersebut tidak pernah sampai ke browser.
- Setelah login, access token, refresh token, dan nama user disimpan dalam **cookie HttpOnly yang dienkripsi dan ditandatangani** (ASP.NET Core Data Protection). Browser tidak bisa membaca maupun mengubahnya.
- Tepat setelah login, gateway memanggil `GET /auth/authorization`. API assessment baru menerima token baru di endpoint fitur setelah panggilan ini dilakukan (lihat [Temuan](#temuan-tentang-api-assessment)).
- Access token berlaku satu jam. Gateway me-refresh token sesaat sebelum kedaluwarsa. Jika mendapat `401`, gateway lebih dulu mengaktifkan ulang token lalu mencoba lagi. Jika masih gagal, token di-refresh satu kali memakai cookie `RefreshToken`, lalu dicoba sekali lagi.

## Tech stack

- .NET 8, ASP.NET Core Web API (controller)
- Tanpa package runtime pihak ketiga: logging, rate limiting, data protection, dan health check semuanya bawaan framework
- Test: xUnit dan `Microsoft.AspNetCore.Mvc.Testing` (integration test in-memory dengan API assessment palsu)

---

## Memulai

### Prasyarat

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`dotnet --version` harus menampilkan 8.x)

Jalankan semua perintah di bawah dari root repositori.

### 1. Atur kredensial

Kredensial aplikasi dari brief assessment disimpan di file `.env` (di-ignore oleh git) di dalam folder project:

```bash
cp src/TrackLab.Api/.env.example src/TrackLab.Api/.env
# lalu edit src/TrackLab.Api/.env:
#   UpstreamApi__AppId=<app_id dari brief>
#   UpstreamApi__AppKey=<app_key dari brief>
```

Environment variable asli menimpa isi `.env`, jadi di CI atau production `UpstreamApi__AppId` / `UpstreamApi__AppKey` bisa langsung di-set sebagai environment variable. Jika salah satu nilai tidak ada, aplikasi menolak start dengan pesan yang jelas.

### 2. Menjalankan

```bash
dotnet run --project src/TrackLab.Api
```

API berjalan di **http://localhost:5080**. Cek dengan:

```bash
curl http://localhost:5080/health   # -> Healthy
```

Setelah itu jalankan frontend dan login dengan akun dari brief (`assessment.it`).

### 3. Migrasi database

**Tidak diperlukan.** Semua data bisnis tersimpan di API assessment. Service ini hanya menyimpan dua file lokal yang dibuat otomatis saat pertama kali dijalankan:

| File | Isi |
|---|---|
| `src/TrackLab.Api/logs/tracklab-YYYYMMDD.jsonl` | Log terstruktur, satu objek JSON per baris, satu file per hari (disimpan 14 hari) |
| `src/TrackLab.Api/data/audit-trail.jsonl` | Audit trail append-only |

Audit trail berada di balik interface `IAuditTrailStore`. Untuk memindahkannya ke database (misalnya EF Core dengan SQL Server), cukup menambah satu class dan mengubah satu baris di `Program.cs`.

### 4. Testing

```bash
dotnet test
```

- **Unit test**: parsing token, pemetaan header paging, diff audit, parsing `.env`, validasi correlation ID.
- **Integration test**: menjalankan aplikasi asli di memori dengan `WebApplicationFactory` dan mengganti API assessment dengan versi palsu yang merekam setiap request. Cakupannya: login dan cookie, CSRF, validasi, header paging, `createdBy`/`updatedBy` dari sesi, soft vs hard delete, entri audit, refresh token dan retry, error dari upstream, logout, dan penerimaan log frontend.

---

## Referensi API

Semua endpoint kecuali login, logout, client log, dan health memerlukan sesi. Request yang mengubah data (`POST`, `PUT`, `DELETE`) wajib mengirim `X-Requested-With: XMLHttpRequest` (proteksi CSRF; lihat [Keamanan](#keamanan)).

### Auth

| Method | Path | Keterangan |
|---|---|---|
| POST | `/api/auth/login` | `{ "username", "password" }`. Mengatur cookie sesi dan mengembalikan `{ username, displayName }` |
| POST | `/api/auth/logout` | Logout di upstream dan menghapus cookie |
| GET | `/api/auth/me` | User yang sedang login, beserta role dan modul dari `/auth/authorization` |
| POST | `/api/auth/refresh` | Refresh sekarang (biasanya otomatis) |

### Tracker, request, tracking

`{resource}` adalah `trackers`, `requests`, atau `trackings`.

| Method | Path | Panggilan upstream | Catatan |
|---|---|---|---|
| GET | `/api/{resource}?offset&limit&sortBy&sortOrder&search` | `GET /{resource}/paging` | Nilai query dikirim sebagai header `X-Paging-*`. `limit` bernilai 1–100 dan `sortBy` harus nama property. |
| GET | `/api/{resource}/advanced?…&include&exclude&operator&ignoreCase` | `GET /{resource}/paging/advance` | `include`/`exclude` berupa JSON seperti `{"last_status":"A\|\|B"}`; backend meng-encode-nya ke Base64 |
| GET | `/api/{resource}/all` | `GET /{resource}` | Tanpa paging, untuk dropdown |
| GET | `/api/{resource}/{id}` | `GET /{resource}/{id}` | |
| POST | `/api/{resource}` | `POST /{resource}` | `id` dan `createdBy` diisi oleh server |
| PUT | `/api/{resource}/{id}` | `PUT /{resource}` | `updatedBy` diisi oleh server |
| DELETE | `/api/{resource}/{id}` | `PUT /{resource}` dengan `isActive: false` | Menonaktifkan record (soft delete). Endpoint DELETE milik API assessment tidak berfungsi (lihat Temuan). Dengan `UpstreamApi:UseDeleteEndpoint=true`, backend memanggil `DELETE /{resource}` dan mendukung `?hard=true`. |
| GET | `/api/trackers/code/{code}` | `GET /trackers/code/{code}` | |
| GET | `/api/requests/{id}/trackings` | `GET /trackings/request-id/{id}` | Progres tracking satu request |

Respons sukses diteruskan apa adanya. Error dikembalikan dalam format **ProblemDetails** RFC 9457 dengan `traceId`:

| Kondisi | Status |
|---|---|
| Input tidak valid | `400` dengan error per field (API assessment tidak dipanggil) |
| Tidak ada sesi, atau sesi sudah kedaluwarsa dan tidak bisa di-refresh | `401` |
| Header CSRF tidak ada | `403` |
| Error dari API assessment (5xx) | `502` (detail upstream dicatat di log, tidak dikembalikan) |
| API assessment tidak bisa dihubungi atau terlalu lambat | `502` / `504` |
| Terlalu banyak percobaan login | `429` |

### Logging dan audit trail

| Method | Path | Keterangan |
|---|---|---|
| POST | `/api/logs/client` | Error/event dari frontend `{ level, message, url?, stack?, context? }` (anonim, dengan rate limit) |
| GET | `/api/logs?date&minLevel&search&correlationId&limit` | Membaca entri log, terbaru lebih dulu |
| GET | `/api/audit-trails?entity&entityId&user&action&offset&limit` | Entri audit, terbaru lebih dulu |
| GET | `/api/audit-trails/{id}` | Satu entri audit |
| GET | `/health` | Health check |

---

## Logging

- Setiap request mendapat **correlation ID**: diambil dari header request `X-Correlation-Id`, atau dibuat baru. ID ini dikembalikan di respons dan ditempelkan ke setiap baris log selama request tersebut diproses, termasuk panggilan ke upstream. Frontend mengirim ID-nya sendiri di setiap panggilan, sehingga satu ID menghubungkan error di browser, request di backend, dan panggilan ke API assessment.
- Log ditulis ke console dan ke `logs/tracklab-YYYYMMDD.jsonl`:
  ```json
  {"timestamp":"2026-11-16T13:48:42+07:00","level":"Information","category":"TrackLab.Api.Logging.RequestLoggingMiddleware","message":"HTTP GET /api/trackers responded 200 in 31 ms for assessment.it from 127.0.0.1","correlationId":"dde5178a…","properties":{"Method":"GET","StatusCode":200,"ElapsedMs":31,…}}
  ```
- Yang dicatat: setiap request HTTP (method, path, status, durasi, user, IP), setiap panggilan upstream (status, durasi), event login/logout/refresh, event audit, error dari frontend, dan exception yang tidak tertangani.
- Yang **tidak** dicatat: body request, password, token, dan header.
- Penulisan log masuk ke antrean yang ditulis ke disk oleh background task, sehingga logging tidak memperlambat request. File yang lebih lama dari 14 hari dihapus.

## Audit trail

Untuk setiap create, update, atau delete yang berhasil, backend menyimpan:

```json
{
  "id": "…", "timestamp": "2026-11-16T06:48:10Z", "user": "assessment.it",
  "action": "Update", "entity": "tracker", "entityId": "…",
  "changes": [{ "field": "description", "oldValue": "QA Checked", "newValue": "QA Checked & Approved" }],
  "before": { … }, "after": { … },
  "correlationId": "…", "clientIp": "…", "userAgent": "…"
}
```

Snapshot "before" dibaca dari API assessment tepat sebelum perubahan, sehingga diff mencerminkan data yang benar-benar tersimpan. Entri hanya ditambahkan, tidak pernah diubah. Jika penulisan entri audit gagal, kegagalan itu dicatat di log, tetapi perubahan dari user tetap berhasil.

## Keamanan

| Aspek | Penanganan |
|---|---|
| Rahasia | `app_id` / `app_key` hanya ada di server (`.env` atau environment variable, di-ignore oleh git) |
| Pencurian token (XSS) | Token disimpan di cookie HttpOnly, tidak pernah di JavaScript atau `localStorage` |
| Manipulasi cookie | Cookie dienkripsi dan ditandatangani (Data Protection); cookie yang diubah dianggap logout |
| CSRF | Cookie `SameSite=Strict`, ditambah header wajib `X-Requested-With` pada request yang mengubah data |
| Brute force | Login dibatasi 10 percobaan per menit per IP |
| Injeksi ke header upstream dan log | Nilai paging dan sort divalidasi, filter harus berupa objek JSON datar, correlation ID dibatasi ke `[A-Za-z0-9-]`, dan baris baru dibuang dari pesan client log |
| Mass assignment | `createdBy` / `updatedBy` / `deletedBy` dan `id` diisi oleh server, tidak pernah diambil dari client |
| Kebocoran error | Body 5xx dari upstream dan detail exception dicatat di log, tidak dikembalikan |
| Default deny | Setiap endpoint memerlukan sesi kecuali ditandai `[AllowAnonymous]` |
| Header | `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Cache-Control: no-store` |

## Konfigurasi

`src/TrackLab.Api/appsettings.json` (nilai apa pun bisa ditimpa dengan environment variable, memakai `__` sebagai pengganti `:`):

| Key | Default | Arti |
|---|---|---|
| `UpstreamApi:BaseUrl` | `https://api-assessment-biopharma.kalbe.co.id` | API assessment |
| `UpstreamApi:AuthPath` / `FeaturePath` | `/api/v1/authentication` / `/api/v1/feature` | Base path |
| `UpstreamApi:AppId` / `AppKey` | *(wajib, lewat `.env`)* | Kredensial aplikasi |
| `UpstreamApi:AppCode` / `ModuleCode` | `ASSESSMENT` / `ALL` | Dikirim saat login dan logout |
| `UpstreamApi:RefreshCookieName` | `RefreshToken` | Cookie yang dibaca endpoint refresh |
| `UpstreamApi:UseDeleteEndpoint` | `false` | Memakai endpoint DELETE milik API, bukan menonaktifkan lewat PUT |
| `UpstreamApi:TimeoutSeconds` | `25` | Timeout upstream (dibuat di bawah batas 30 detik milik proxy Next.js) |
| `SessionCookies:Secure` | `true` (`false` di Development) | Cookie hanya dikirim lewat HTTPS |
| `Cors:AllowedOrigins` | `["http://localhost:3000"]` | Hanya diperlukan jika frontend memanggil API secara langsung |
| `FileLogging:Directory` / `MinimumLevel` / `RetainedDays` | `logs` / `Information` / `14` | Log file |
| `AuditTrail:FilePath` | `data/audit-trail.jsonl` | File audit trail |

## Struktur project

```
src/TrackLab.Api/
├── Program.cs              # Composition root: registrasi service dan urutan middleware
├── Controllers/            # Auth, Trackers, Requests, Trackings, AuditTrails, Logs
├── Contracts/              # DTO request dengan atribut validasi
├── Upstream/               # Typed HttpClient, handler kredensial/logging, gateway refresh token, header paging
├── Auth/                   # Sesi cookie terenkripsi + authentication handler ASP.NET Core
├── Audit/                  # Model audit, diff, recorder, penyimpanan JSON Lines
├── Logging/                # Correlation ID, request logging, file logger JSON + pembaca log
├── Security/               # Cek header CSRF, security headers, policy rate limit
└── Errors/                 # Global exception handler (ProblemDetails)
tests/TrackLab.Api.Tests/
├── Unit/                   # Logika murni
├── Integration/            # Seluruh aplikasi di memori melawan API assessment palsu
└── Support/                # Test factory, upstream palsu, helper JWT
```

## Temuan tentang API assessment

Postman collection tidak menyertakan contoh respons, jadi perilaku API diperiksa secara manual. Backend dibangun berdasarkan hasil pemeriksaan tersebut:

| Temuan | Penanganan |
|---|---|
| Login mengembalikan `{ data: { token: { access, refresh, expiresIn: 3600 } } }` dan juga mengatur cookie `RefreshToken` | Kedua token disimpan di sesi terenkripsi. Panggilan refresh mengirim cookie `RefreshToken` (dengan token dari body sebagai cadangan). |
| **Token baru selalu mendapat `401` di semua endpoint fitur sampai `GET /auth/authorization` dipanggil dengan token tersebut** | Dipanggil otomatis tepat setelah login dan setelah setiap refresh. Saat mendapat `401`, gateway mengaktifkan ulang token sebelum mencoba refresh. |
| `X-Paging-SortBy` dipakai sebagai kolom `ORDER BY` di SQL: nilai kosong atau kolom yang tidak dikenal menghasilkan HTTP 500. Nama kolomnya tidak sepenuhnya sesuai diagram (request tidak punya `created_on`; kolom tanggalnya `request_date`) | Backend selalu mengirim kolom sort (default `id`) dan hanya menerima nama property biasa. Jika API menolak kolomnya (error Postgres 42703), backend mengambil ulang halaman dengan sort `id` dan mengatur `X-Sort-Fallback`, sehingga user tetap mendapat data. |
| Respons paging berbentuk `{ data: { items, totalItems, totalPages, pageCurrent, ... } }` | Diteruskan apa adanya; frontend membaca `items` dan `totalItems`. |
| Mengirim `null` untuk beberapa field opsional gagal (misalnya `"mandays": null` pada tracker menghasilkan *"could not be converted to System.Int32"*), sedangkan tidak menyertakan field tersebut berhasil | Field opsional yang kosong tidak disertakan dalam JSON yang dikirim ke API. |
| `DELETE` yang body JSON-nya dikirim secara streaming (chunked, tanpa `Content-Length`) menggantung sampai timeout | Body diserialisasi lebih dulu, sehingga setiap request membawa `Content-Length`. |
| **Setiap `DELETE` gagal dengan 400 "A non-empty request body is required."**: body tidak pernah sampai ke API, bahkan dari Postman, jadi body sudah dibuang sebelum diterima API. Request berikutnya pada koneksi yang sama bisa menggantung. | Secara default, delete dilakukan sebagai `PUT` dengan `isActive: false` (dibangun dari data record saat ini) dan dicatat di audit trail sebagai delete. Delete permanen tidak mungkin dilakukan lewat API ini, jadi UI tidak menyediakannya. Jika API sudah diperbaiki, `UpstreamApi:UseDeleteEndpoint=true` mengembalikan ke DELETE (dengan `Connection: close`, dan kembali menonaktifkan record jika body dibuang lagi). |
| Membuat request dengan project code yang sudah ada, bahkan yang sudah dihapus, menghidupkan kembali record lama (id sama), bukan membuat record baru | Didokumentasikan. Audit trail mencatat id yang dikembalikan API, sehingga kedua "create" muncul di riwayat record yang sama. |
| Read di-cache (stack trace menunjukkan adanya cache interceptor), sehingga membaca record tepat setelah diubah bisa mengembalikan nilai lama | Frontend langsung menampilkan nilai yang disimpan, tanpa mengandalkan read ulang seketika. |
| Respons error berisi stack trace dan detail SQL | Body 5xx dari upstream dicatat di log dan diganti dengan `502` generik. |
| Akun test dan datanya dipakai bersama oleh semua kandidat (record seperti `dika` atau `LIVE-TEST-2007`) | Validasi menerima data yang sudah ada (kode huruf kecil, parent code kosong), dan UI bisa menangani nama ikon yang gayanya beragam. |

Catatan lain:
- `createdBy` / `updatedBy` / `deletedBy` berisi username yang sedang login.
- Tidak ada endpoint partner (request hanya membawa `partnerId`). Lihat README frontend untuk cara nama perusahaan ditampilkan.
