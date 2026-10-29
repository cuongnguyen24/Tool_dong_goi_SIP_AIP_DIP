# Source phục hồi từ bản EXE

Phục hồi trực tiếp từ `../DongGoiAIP_SIP_HuongDan/AXE.PackagingTool.exe` bằng ILSpy.
Đây là source dịch ngược, không phải bản source gốc; tên biến cục bộ và chú thích không thể khôi phục đầy đủ.

Mở `AXE.PackagingTool.Recovered.sln` trong Visual Studio và chọn project AXE.PackagingTool làm startup project.

Build:

```powershell
dotnet build AXE.PackagingTool.Recovered.sln -c Release
```

Chạy `bin/Release/net8.0-windows/AXE.PackagingTool.exe`. Giữ toàn bộ thư mục output, bao gồm các DLL, Metadata, MauExcel và file cấu hình. Bản này cần .NET 8 Desktop Runtime x64.

## Nội dung giữ từ EXE

- Giao diện cột gộp hồ sơ FileCodehs, SIP/AIP/DIP, đồng bộ Bìa/Mục lục.
- Bảng log, thống kê hồ sơ và xuất Excel.
- Logic đọc Excel, kiểm tra và đóng gói lấy từ cùng EXE.
- Thư viện trong `lib` lấy từ chính EXE để build ngoại tuyến; có thể dùng NuGet bằng `-p:UseBundledDependencies=false`.
- Metadata/DanhMuc.xlsx lấy từ thư mục bản chạy; MauExcel/Template.xlsx lấy từ ZIP đi kèm vì thư mục bản chạy thiếu mẫu.

## Điều chỉnh để build lại

- Khai báo lại điểm khởi động WPF và đường dẫn cửa sổ chính.
- Chuyển hai mảng phần mở rộng do trình biên dịch sinh ra về cú pháp C# tương đương.
- Sửa App.config: bỏ chú thích XML nằm trong giá trị thuộc tính, nguyên nhân khiến cấu hình cũ không đọc được. Giữ nguyên các cặp ánh xạ.
- Project ở thư mục cha loại trừ RecoveredSource để hai phiên bản không bị biên dịch lẫn nhau.

## Kiểm tra

- Build Release thành công.
- Khởi tạo tài nguyên WPF, cửa sổ chính và bố trí giao diện thành công; kiểm tra bảng log có 5 cột, mặc định FileCodehs và lệnh xuất thống kê.
- App.config đọc được dưới dạng XML.
- Chưa kiểm tra toàn bộ luồng đóng gói, nội dung ZIP và các thao tác chuột trên giao diện.

EXE và cấu hình trong thư mục bản chạy cũ không bị thay thế.
