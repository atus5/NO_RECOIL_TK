# HackTK.Next — Hướng Dẫn Khởi Động & Kiến Trúc Bản Phát Hành (Release)

## 1. Yêu cầu môi trường (Runtime Requirements)
- Hệ điều hành: Windows 10/11 x64.
- Runtime bắt buộc: **.NET 10.0 Windows Desktop Runtime (`Microsoft.WindowsDesktop.App 10.0.x`)** và **.NET 10.0 Runtime (`Microsoft.NETCore.App 10.0.x`)**.

## 2. Danh sách tệp thực thi & Assembly phụ thuộc bắt buộc trong gói Release
Trong thư mục phát hành (`dist\controller` / `HackTK.Next.Desktop.zip`) bao gồm đầy đủ 9 assembly của dự án, không bỏ sót bất kỳ module nào:
1. `HackTK.Next.UI.exe` / `HackTK.Next.UI.dll` (+ `.deps.json`, `.runtimeconfig.json`) — Ứng dụng giao diện điều khiển Desktop (WinForms).
2. `HackTK.Next.Runtime.exe` / `HackTK.Next.Runtime.dll` (+ `.deps.json`, `.runtimeconfig.json`) — Tiến trình **Authorized Test Runtime** độc lập tiếp nhận lệnh, chạy `EngineHost` và 5 module xử lý.
3. `HackTK.Next.Abstractions.dll` — Hợp đồng giao diện (`IWeaponSnapshot`, `IActorSnapshot`, `IWorldStateProvider`, `IWeaponParameterResolver`, `IEngineModule`, ...).
4. `HackTK.Next.Configuration.dll` — Quản lý cấu hình XML nguyên tử (`EngineProfile`, `AtomicXmlConfigurationStore`, `SessionLeaseManager`, giao thức IPC v3).
5. `HackTK.Next.Diagnostics.dll` — Ghi nhận chẩn đoán có cấu trúc & phân tích đường đạn (`StructuredDiagnosticLogger`, `BallisticAuditAnalyzer`).
6. `HackTK.Next.Services.dll` — Dịch vụ tính toán (`NewtonSpreadSolverService`, `DualOriginVisibilityService`, `SubpixelAimComputationService`, `ResourceOwnershipServices`).
7. `HackTK.Next.Core.dll` — Nhân điều phối (`ServiceContainer`, `ModuleManager`, `EngineHost`).
8. `HackTK.Next.Modules.dll` — 5 module chức năng (`SpreadCompensationModule`, `TargetAcquisitionModule`, `EspProjectionModule`, `MaterialSlotModule`, `MovementPulseModule`).
9. `HackTK.Next.Simulation.dll` — `EngineModuleLoader`, `WeaponRegistry`, `RuntimeWeaponParameterResolver`, `WeaponCategorySimulationEngine` (7 nhóm vũ khí), `AuthorizedTestRuntimeHost`.
10. `profiles\default.xml` — Tệp cấu hình mặc định hợp lệ.

## 3. Hướng dẫn khởi động & kiểm chứng đầu cuối (End-to-End)

### Cách 1: Khởi động trực tiếp từ giao diện Desktop (`HackTK.Next.UI.exe`)
1. Chạy `HackTK.Next.UI.exe`.
2. Trên thanh kết nối phía trên cùng:
   - Nhấn **"Bật Runtime Thử Nghiệm"** để khởi chạy tiến trình độc lập `HackTK.Next.Runtime.exe` (hoặc nếu bạn đang chạy môi trường Unity thử nghiệm đã tích hợp `UnityRuntimeBridgeHost`, nhấn **"Tìm game"**).
3. Chọn tiến trình trong danh sách và nhấn **"Kết nối"**:
   - UI gửi gói lệnh kết nối qua `%LocalAppData%\HackTKRebuild\sessions\<PID>\command.xml` và **chờ xác nhận (ACK)** từ `status.xml` của tiến trình runtime.
4. Bật các tùy chọn cần kiểm tra (ví dụ tab **Aim**: bật `No spread`, `No recoil`, `Aim`; tab **Visuals**: `ESP hộp`, `Chams`; tab **Misc**: `Bhop`) rồi nhấn **"Áp dụng vào module"**:
   - Nút Apply chỉ báo thành công sau khi tiến trình runtime đã đọc lệnh, nạp cấu hình vào `EngineHost`, kích hoạt các module và ghi xác nhận `AppliedRevision` khớp số hiệu lệnh.
5. Chuyển sang tab **"Settings / Trạng thái"** để quan sát trạng thái thực thi thời gian thực (`Active` / `Idle`, số phát đạn đã bù, vũ khí đang xử lý, sai số lượng tử hóa, số bước chuột Aim, số lệnh vẽ ESP, số khe Chams).

### Cách 2: Kiểm tra qua dòng lệnh (CLI Self-Check & Smoke Test)
```powershell
# Kiểm tra nạp assembly & tự kiểm tra tiến trình Runtime
.\HackTK.Next.Runtime.exe --self-check

# Chạy kiểm tra giao diện & lưu/đọc cấu hình Desktop UI
.\HackTK.Next.UI.exe --smoke-test .\smoke-out
```

## 4. Lưu ý minh bạch về tích hợp Unity thực tế
- Tiến trình `HackTK.Next.Runtime.exe` là **Authorized Standalone Test Runtime** (môi trường thử nghiệm độc lập chạy trên .NET 10).
- Để tích hợp vào một dự án game Unity do bạn sở hữu mã nguồn (ví dụ `SSJJ_BattleClient_Unity`), phía Unity cần cung cấp một lớp thực thi giao diện `HackTK.Next.Runtime.IUnityRuntimeHostAdapter` (cầu nối `Update`/`OnGUI`, camera, xương nhân vật và raycast của Unity sang `IWorldStateProvider`) và gọi `UnityRuntimeBridgeHost.TickUnityFrame()` trên luồng chính của Unity.
- Dự án **không** chứa mã tiêm DLL (DLL injection), không sửa bộ nhớ tiến trình bên thứ ba và không vượt cơ chế bảo vệ/anti-cheat.
