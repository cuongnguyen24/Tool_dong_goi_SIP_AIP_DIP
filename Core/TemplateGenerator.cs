using ClosedXML.Excel;
using AXE.PackagingTool.Models;

namespace AXE.PackagingTool.Core;

/// <summary>
/// Sinh file Excel template (.xlsx) theo từng loại gói (AIP / SIP / DIP).
/// Template được tạo hoàn toàn trong code — không cần file nhúng sẵn.
/// Mỗi sheet có: header row (bold, màu nền), dropdown validation, ghi chú cột.
/// </summary>
public static class TemplateGenerator
{
    // ─── Màu sắc header ────────────────────────────────────────────────────
    private static readonly XLColor ColHeader    = XLColor.FromHtml("#1F4E79"); // xanh đậm
    private static readonly XLColor ColRequired  = XLColor.FromHtml("#FFDDC1"); // cam nhạt
    private static readonly XLColor ColOptional  = XLColor.FromHtml("#EBF5FB"); // xanh nhạt
    private static readonly XLColor ColPath      = XLColor.FromHtml("#D5F5E3"); // xanh lá nhạt
    private static readonly XLColor ColInfo      = XLColor.FromHtml("#F9F9F9");

    // ─── Public ────────────────────────────────────────────────────────────

    public static void Generate(PackageType type, string outputPath)
    {
        using var wb = new XLWorkbook();

        // Sheet HUONG_DAN luôn đứng đầu
        AddInstructionSheet(wb, type);

        // Sheet PHONG — chung cho tất cả loại gói
        AddPhongSheet(wb);

        switch (type)
        {
            case PackageType.AIP:
                AddHoSoSheet(wb);
                AddVanBanSheet(wb);
                AddAnhPhimSheet(wb);
                AddGhiAmVideoSheet(wb);
                break;

            case PackageType.SIP:
                AddHoSoSheet(wb);
                AddVanBanSheet(wb);
                AddAnhPhimSheet(wb);
                AddGhiAmVideoSheet(wb);
                AddMucLucSheet(wb);
                break;

            case PackageType.DIP:
                AddVanBanSheet(wb);
                AddAnhPhimSheet(wb);
                AddGhiAmVideoSheet(wb);
                AddDipXacThucSheet(wb);
                break;
        }

        wb.SaveAs(outputPath);
    }

    // ─── Sheet: HUONG_DAN ─────────────────────────────────────────────────

    private static void AddInstructionSheet(XLWorkbook wb, PackageType type)
    {
        var ws = wb.Worksheets.Add("HUONG_DAN");
        ws.TabColor = XLColor.Orange;

        ws.Cell("A1").Value = $"HƯỚNG DẪN SỬ DỤNG — Template {type.ToDisplayName()}";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Cell("A1").Style.Font.FontColor = XLColor.DarkRed;

        int r = 3;
        var lines = new[]
        {
            "CẤU TRÚC HEADER (2 DÒNG ĐẦU):",
            "  Dòng 1 — Mã trường (code): tên kỹ thuật ngắn, dùng để phần mềm nhận diện cột. KHÔNG xóa hoặc sửa.",
            "  Dòng 2 — Tên trường (mô tả): tên tiếng Việt đầy đủ, chỉ để đọc. Không dùng để xử lý.",
            "  Dữ liệu điền từ DÒNG 3 trở đi.",
            "",
            "MÀU NỀN CỘT:",
            "  Nền CAM NHẠT  (★) = BẮT BUỘC phải điền.",
            "  Nền XANH NHẠT     = Tùy chọn, có thể để trống.",
            "  Nền XANH LÁ       = Đường dẫn file (Path) — xem hướng dẫn phía dưới.",
            "",
            "CỘT PATH (đường dẫn tương đối đến file nguồn):",
            "  Nhập đường dẫn TƯƠNG ĐỐI từ Thư mục gốc đến file tài liệu.",
            "  Ví dụ: Thư mục gốc = D:\\TaiLieu\\G09",
            "          File nằm ở  = D:\\TaiLieu\\G09\\QuiII\\QD123.pdf",
            "          → Cột Path  = QuiII\\QD123.pdf",
            "  Nếu file nằm ngay trong Thư mục gốc → để trống cột Path.",
            "",
            "CỘT TepTin:",
            "  Tên file đích sẽ lưu trong gói ZIP (không có đường dẫn, chỉ là tên file).",
            "  Ví dụ: QD123.pdf",
            "",
            "LIÊN KẾT DỮ LIỆU GIỮA CÁC SHEET:",
            "  Cột MaPhong trong sheet HOSO và TAILIEU_* phải khớp với sheet PHONG.",
            "  Cột MaHoSo trong sheet TAILIEU_* phải khớp với sheet HOSO (nếu có hồ sơ).",
            "",
            "ĐỊNH DẠNG NGÀY THÁNG:",
            "  Nhập theo dạng YYYY-MM-DD, ví dụ: 2024-03-15",
            "  Cũng chấp nhận: YYYY-MM hoặc YYYY (cho trường năm chỉ có năm).",
        };

        foreach (var line in lines)
        {
            ws.Cell(r, 1).Value = line;
            if (line.StartsWith("CẤU TRÚC") || line.StartsWith("MÀU NỀN") ||
                line.StartsWith("CỘT PATH") || line.StartsWith("CỘT TepTin") ||
                line.StartsWith("LIÊN KẾT") || line.StartsWith("ĐỊNH DẠNG"))
            {
                ws.Cell(r, 1).Style.Font.Bold = true;
                ws.Cell(r, 1).Style.Font.FontColor = XLColor.DarkBlue;
            }
            r++;
        }

        ws.Column(1).Width = 90;
        ws.SheetView.FreezeRows(0);
    }

    // ─── Sheet: PHONG ─────────────────────────────────────────────────────

    private static void AddPhongSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("PHONG");
        ws.TabColor = XLColor.DarkBlue;

        var cols = new (string Key, string Label, bool Required, string? Note)[]
        {
            ("MaPhong",      "Mã phông/công trình/sưu tập",    true,  "Mã duy nhất, VD: G09"),
            ("TenPhong",     "Tên phông/công trình/sưu tập",   true,  null),
            ("TrangThai",    "Trạng thái",                     true,  "mở / đóng"),
            ("LichSu",       "Lịch sử đơn vị hình thành phông",true,  null),
            ("ThoiGianBD",   "Thời gian tài liệu (bắt đầu)",  true,  "YYYY hoặc YYYY-MM-DD"),
            ("ThoiGianKT",   "Thời gian tài liệu (kết thúc)", false, "YYYY hoặc YYYY-MM-DD"),
            ("TongSo",       "Tổng số tài liệu",              true,  "Số nguyên"),
            ("PhanLoai",     "Phương án phân loại",           true,  null),
            ("NgonNgu",      "Ngôn ngữ",                      true,  "ISO 639: vi, en, fr"),
            ("CheDoDuPhong", "Chế độ dự phòng",               false, null),
            ("GhiChu",       "Ghi chú",                       false, null),
        };

        ApplyHeaders(ws, cols);
        AddDropdown(ws, FindCol(cols, "TrangThai"), "mở,đóng");
        FreezeAndAutoFit(ws);
    }

    // ─── Sheet: HOSO ──────────────────────────────────────────────────────

    private static void AddHoSoSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("HOSO");
        ws.TabColor = XLColor.DarkGreen;

        var cols = new (string Key, string Label, bool Required, string? Note)[]
        {
            ("MaPhong",          "Mã phông",                   true,  "Khớp với sheet PHONG"),
            ("MaHoSo",           "Mã hồ sơ",                   true,  "Mã duy nhất trong phông"),
            ("TieuDe",           "Tiêu đề hồ sơ",              true,  null),
            ("ThoiHanLuu",       "Thời hạn lưu trữ",           true,  "5 năm / 10 năm / Vĩnh viễn"),
            ("MucDoTiepCan",     "Mức độ tiếp cận",            true,  "Công khai / Hạn chế / Mật / Tuyệt mật"),
            ("NgonNgu",          "Ngôn ngữ",                   true,  "vi / en"),
            ("ThoiGianBD",       "Thời gian bắt đầu",         true,  "YYYY-MM-DD"),
            ("ThoiGianKT",       "Thời gian kết thúc",        true,  "YYYY-MM-DD"),
            ("TuKhoa",           "Từ khóa",                    false, "Phân cách bằng ;"),
            ("TongSo",           "Tổng số tài liệu",          true,  "Số nguyên"),
            ("SoTo",             "Số lượng tờ (số hóa)",       false, null),
            ("SoTrang",          "Số lượng trang",             true,  "Số nguyên"),
            ("TinhTrangVatLy",   "Tình trạng vật lý (số hóa)", false, "Tốt / Trung bình / Kém"),
            ("KyHieuThongTin",   "Ký hiệu thông tin",          false, null),
            ("MucDoTinCay",      "Mức độ tin cậy",             false, "Cao / Trung bình / Thấp"),
            ("MaGoc",            "Mã hồ sơ gốc giấy",          false, "Chỉ với hồ sơ số hóa"),
            ("CheDoDuPhong",     "Chế độ lập TLLT dự phòng",  false, null),
            ("TinhTrangDuPhong", "Tình trạng lập TLLT dự phòng",false,null),
            ("TepTin",           "Tệp tin hồ sơ",              false, "Tên file nếu toàn hồ sơ 1 file"),
            ("GhiChu",           "Ghi chú",                    false, null),
        };

        ApplyHeaders(ws, cols);
        AddDropdown(ws, FindCol(cols, "MucDoTiepCan"), "Công khai,Hạn chế,Mật,Tuyệt mật");
        AddDropdown(ws, FindCol(cols, "TinhTrangVatLy"), "Tốt,Trung bình,Kém");
        AddDropdown(ws, FindCol(cols, "MucDoTinCay"), "Cao,Trung bình,Thấp");
        FreezeAndAutoFit(ws);
    }

    // ─── Sheet: TAILIEU_VANBANHANHCHINH ───────────────────────────────────

    private static void AddVanBanSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("TAILIEU_VANBANHANHCHINH");
        ws.TabColor = XLColor.DarkOrange;

        var cols = new (string Key, string Label, bool Required, string? Note)[]
        {
            ("MaPhong",          "Mã phông",                    true,  null),
            ("MaHoSo",           "Mã hồ sơ",                    false, "Để trống nếu tài liệu rời lẻ"),
            ("MaDinhDanh",       "Mã định danh tài liệu",       true,  "UUID hoặc mã nội bộ"),
            ("MaLuuTru",         "Mã lưu trữ tài liệu",         true,  null),
            ("ThoiHanLuu",       "Thời hạn lưu trữ",            true,  null),
            ("LoaiTaiLieu",      "Tên loại tài liệu",           true,  "Quyết định / Công văn..."),
            ("So",               "Số của tài liệu",             false, null),
            ("KyHieu",           "Ký hiệu của tài liệu",        false, null),
            ("NgayBanHanh",      "Ngày tháng năm ban hành",     true,  "YYYY-MM-DD"),
            ("CoQuan",           "Cơ quan/tổ chức/cá nhân ban hành", true, null),
            ("TrichYeu",         "Trích yếu nội dung",          true,  null),
            ("NgonNgu",          "Ngôn ngữ",                    true,  "vi / en"),
            ("SoTrang",          "Số lượng trang",              true,  null),
            ("SoTo",             "Số lượng tờ (số hóa)",        false, null),
            ("KyHieuThongTin",   "Ký hiệu thông tin",           false, null),
            ("TuKhoa",           "Từ khóa",                     false, "Phân cách bằng ;"),
            ("MucDoTiepCan",     "Mức độ tiếp cận",             true,  null),
            ("MucDoTinCay",      "Mức độ tin cậy",              false, null),
            ("ButTich",          "Bút tích (số hóa)",           false, "Có / Không"),
            ("TinhTrangVatLy",   "Tình trạng vật lý (số hóa)", false, null),
            ("QuyTrinh",         "Quy trình xử lý (gốc ĐT)",   false, null),
            ("CheDoDuPhong",     "Chế độ lập TLLT dự phòng",   false, null),
            ("TinhTrangDuPhong", "Tình trạng lập TLLT dự phòng",false,null),
            ("TepTin",           "Tệp tin tài liệu",            true,  "Tên file đích trong ZIP"),
            ("Path",             "Path",                        false, "Đường dẫn tương đối từ Thư mục gốc → file nguồn"),
            ("GhiChu",           "Ghi chú",                     false, null),
        };

        ApplyHeaders(ws, cols);
        AddDropdown(ws, FindCol(cols, "MucDoTiepCan"), "Công khai,Hạn chế,Mật,Tuyệt mật");
        AddDropdown(ws, FindCol(cols, "MucDoTinCay"),  "Cao,Trung bình,Thấp");
        AddDropdown(ws, FindCol(cols, "ButTich"),      "Có,Không");
        FreezeAndAutoFit(ws);
    }

    // ─── Sheet: TAILIEU_ANHANH ────────────────────────────────────────────

    private static void AddAnhPhimSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("TAILIEU_ANHANH");
        ws.TabColor = XLColor.Purple;

        var cols = new (string Key, string Label, bool Required, string? Note)[]
        {
            ("MaPhong",          "Mã phông",                   true,  null),
            ("MaHoSo",           "Mã hồ sơ",                   false, "Để trống nếu tài liệu rời lẻ"),
            ("MaLuuTru",         "Mã lưu trữ tài liệu",        true,  null),
            ("ThoiHanLuu",       "Thời hạn lưu trữ",           true,  null),
            ("PhanLoai",         "Phân loại",                  true,  "âm bản gốc / dương bản"),
            ("SoLuuTru",         "Số lưu trữ",                 true,  null),
            ("KyHieuThongTin",   "Ký hiệu thông tin",          false, null),
            ("TenSuKien",        "Tên sự kiện",                true,  null),
            ("TieuDe",           "Tiêu đề phim/ảnh",          true,  null),
            ("TacGia",           "Tác giả",                    true,  null),
            ("DiaDiem",          "Địa điểm chụp",             true,  null),
            ("ThoiGianChup",     "Thời gian chụp",            true,  "YYYY-MM-DD"),
            ("MauSac",           "Màu sắc",                    true,  "Màu / Đen trắng"),
            ("Co",               "Cỡ phim/ảnh",               true,  "A4 / 35mm ..."),
            ("TaiLieuDiKem",     "Tài liệu đi kèm",           false, null),
            ("MucDoTiepCan",     "Mức độ tiếp cận",           true,  null),
            ("TinhTrangVatLy",   "Tình trạng vật lý",         false, null),
            ("CheDoDuPhong",     "Chế độ lập TLLT dự phòng",  false, null),
            ("TinhTrangDuPhong", "Tình trạng lập TLLT dự phòng",false,null),
            ("TepTin",           "Tệp tin tài liệu",          true,  "Tên file đích trong ZIP"),
            ("Path",             "Path",                      false, "Đường dẫn tương đối từ Thư mục gốc → file nguồn"),
            ("GhiChu",           "Ghi chú",                   false, null),
        };

        ApplyHeaders(ws, cols);
        AddDropdown(ws, FindCol(cols, "PhanLoai"),      "âm bản gốc,dương bản");
        AddDropdown(ws, FindCol(cols, "MauSac"),        "Màu,Đen trắng");
        AddDropdown(ws, FindCol(cols, "MucDoTiepCan"),  "Công khai,Hạn chế,Mật,Tuyệt mật");
        FreezeAndAutoFit(ws);
    }

    // ─── Sheet: TAILIEU_GHIAM_VIDEO ───────────────────────────────────────

    private static void AddGhiAmVideoSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("TAILIEU_GHIAM_VIDEO");
        ws.TabColor = XLColor.Red;

        var cols = new (string Key, string Label, bool Required, string? Note)[]
        {
            ("MaPhong",          "Mã phông",                    true,  null),
            ("MaHoSo",           "Mã hồ sơ",                    false, "Để trống nếu tài liệu rời lẻ"),
            ("MaLuuTru",         "Mã lưu trữ tài liệu",         true,  null),
            ("ThoiHanLuu",       "Thời hạn lưu trữ",            true,  null),
            ("PhanLoai",         "Phân loại",                   true,  "âm thanh / video"),
            ("SoLuuTru",         "Số lưu trữ",                  true,  null),
            ("KyHieuThongTin",   "Ký hiệu thông tin",           false, null),
            ("TenSuKien",        "Tên sự kiện",                 true,  null),
            ("TieuDe",           "Tiêu đề âm thanh/video",     true,  null),
            ("TacGia",           "Tác giả",                     true,  null),
            ("DiaDiem",          "Địa điểm",                    true,  null),
            ("ThoiGian",         "Thời gian",                   true,  "YYYY-MM-DD"),
            ("NgonNgu",          "Ngôn ngữ",                    true,  "vi / en"),
            ("ThoiLuong",        "Thời lượng",                  true,  "HH:MM:SS"),
            ("TaiLieuDiKem",     "Tài liệu đi kèm",            false, null),
            ("MucDoTiepCan",     "Mức độ tiếp cận",            true,  null),
            ("ChatLuong",        "Chất lượng âm thanh/video",  false, null),
            ("TinhTrangVatLy",   "Tình trạng vật lý",          false, null),
            ("CheDoDuPhong",     "Chế độ lập TLLT dự phòng",   false, null),
            ("TinhTrangDuPhong", "Tình trạng lập TLLT dự phòng",false, null),
            ("TepTin",           "Tệp tin tài liệu",            true,  "Tên file đích trong ZIP"),
            ("Path",             "Path",                        false, "Đường dẫn tương đối từ Thư mục gốc → file nguồn"),
            ("GhiChu",           "Ghi chú",                     false, null),
        };

        ApplyHeaders(ws, cols);
        AddDropdown(ws, FindCol(cols, "PhanLoai"),     "âm thanh,video");
        AddDropdown(ws, FindCol(cols, "MucDoTiepCan"), "Công khai,Hạn chế,Mật,Tuyệt mật");
        FreezeAndAutoFit(ws);
    }

    // ─── Sheet: MUCLUC (chỉ SIP) ─────────────────────────────────────────

    private static void AddMucLucSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("MUCLUC_NOP");
        ws.TabColor = XLColor.Brown;

        var cols = new (string Key, string Label, bool Required, string? Note)[]
        {
            ("STT",          "STT",                  true,  "Số thứ tự"),
            ("MaHoSo",       "Mã hồ sơ / tài liệu", true,  null),
            ("TieuDe",       "Tiêu đề",              true,  null),
            ("ThoiGianBD",   "Thời gian bắt đầu",   false, "YYYY-MM-DD"),
            ("ThoiGianKT",   "Thời gian kết thúc",  false, "YYYY-MM-DD"),
            ("SoTrang",      "Số trang",             false, null),
            ("MucDoTiepCan", "Mức độ tiếp cận",      false, null),
            ("GhiChu",       "Ghi chú",              false, null),
        };

        ApplyHeaders(ws, cols);
        FreezeAndAutoFit(ws);
    }

    // ─── Sheet: DIP_XACTHUC (chỉ DIP) ────────────────────────────────────

    private static void AddDipXacThucSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("DIP_XACTHUC");
        ws.TabColor = XLColor.DarkRed;

        var cols = new (string Key, string Label, bool Required, string? Note)[]
        {
            ("MaXacThuc",   "Mã xác thực lưu trữ",                true,  "Mã do hệ thống cấp"),
            ("TenLuuTru",   "Tên lưu trữ lịch sử",                true,  null),
            ("HoTen",       "Họ tên người nhận",                   true,  null),
            ("MaDinhDanh",  "Mã định danh công dân / số GTTT",    true,  null),
            ("MaLuuTruGoc", "Mã lưu trữ tài liệu gốc",            true,  null),
            ("SoKyHieu",    "Số và ký hiệu tài liệu gốc",         false, null),
            ("LoaiTaiLieu", "Tên loại tài liệu",                  true,  null),
            ("TrichYeu",    "Trích yếu nội dung / tiêu đề",       true,  null),
            ("MucDich",     "Mục đích sử dụng",                   true,  null),
            ("NgayCap",     "Ngày cấp",                           true,  "YYYY-MM-DD"),
            ("ThoiHan",     "Thời hạn sử dụng",                   true,  null),
            ("SoLuong",     "Số lượng bản",                       true,  "Số nguyên"),
        };

        ApplyHeaders(ws, cols);
        FreezeAndAutoFit(ws);
    }

    // ─── Shared helpers ───────────────────────────────────────────────────

    private static void ApplyHeaders(
        IXLWorksheet ws,
        (string Key, string Label, bool Required, string? Note)[] cols)
    {
        for (int i = 0; i < cols.Length; i++)
        {
            var (key, label, required, note) = cols[i];
            int col = i + 1;

            // ── Dòng 1: Mã trường kỹ thuật (ExcelReader dùng dòng này để nhận cột) ──
            var keyCell = ws.Cell(1, col);
            keyCell.Value = key;
            keyCell.Style.Font.Bold = false;
            keyCell.Style.Font.FontSize = 8;
            keyCell.Style.Font.FontColor = XLColor.FromHtml("#BBBBBB");
            keyCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A252F"); // xanh đen
            keyCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            keyCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            keyCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#2C3E50");

            // ── Dòng 2: Tên trường tiếng Việt (hiển thị cho người dùng) ──────────
            var labelCell = ws.Cell(2, col);
            labelCell.Value = required ? "★ " + label : label;
            labelCell.Style.Font.Bold = true;
            labelCell.Style.Font.FontSize = 10;
            labelCell.Style.Font.FontColor = XLColor.White;
            labelCell.Style.Fill.BackgroundColor = required
                ? XLColor.FromHtml("#1F4E79")  // xanh đậm = bắt buộc
                : XLColor.FromHtml("#2E6E4E");  // xanh lá đậm = tùy chọn
            labelCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            labelCell.Style.Alignment.WrapText = true;
            labelCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            // ── Dòng 3–501: Vùng dữ liệu ────────────────────────────────────────
            var dataRange = ws.Range(3, col, 501, col);
            if (key == "Path")
                dataRange.Style.Fill.BackgroundColor = ColPath;
            else if (required)
                dataRange.Style.Fill.BackgroundColor = ColRequired;
            else
                dataRange.Style.Fill.BackgroundColor = ColOptional;

            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.OutsideBorderColor = XLColor.Gray;
        }
    }

    private static void AddDropdown(IXLWorksheet ws, int col, string items)
    {
        // Dữ liệu bắt đầu từ dòng 3
        ws.Range(3, col, 501, col)
          .SetDataValidation()
          .List($"\"{items}\"", true);
    }

    private static int FindCol(
        (string Key, string Label, bool Required, string? Note)[] cols,
        string key)
    {
        for (int i = 0; i < cols.Length; i++)
            if (cols[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return i + 1;
        return 1;
    }

    private static void FreezeAndAutoFit(IXLWorksheet ws)
    {
        ws.SheetView.FreezeRows(2);   // đóng băng 2 dòng header
        ws.SheetView.FreezeColumns(0);
        ws.Column(1).Width = 18;
        ws.Row(1).Height = 14;        // dòng mã trường: nhỏ gọn
        ws.Row(2).Height = 42;        // dòng tên tiếng Việt: rõ ràng
    }
}
