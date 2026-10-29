using AXE.PackagingTool.Models;

namespace AXE.PackagingTool.Core;

/// <summary>
/// Validate dữ liệu từ PackageData và file vật lý trên đĩa.
/// Trả về danh sách ValidationIssue — ERROR ngăn đóng gói, WARNING bỏ qua được.
/// </summary>
public class Validator
{
    private readonly string _baseFolder;
    private readonly Action<string> _log;

    private static readonly HashSet<string> ValidExtVanBan =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    private static readonly HashSet<string> ValidExtAnhPhim =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".tiff", ".tif", ".png", ".pdf" };

    private static readonly HashSet<string> ValidExtGhiAmVideo =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wma", ".mp4", ".avi", ".wmv", ".wav" };

    public Validator(string baseFolder, Action<string> log)
    {
        _baseFolder = baseFolder;
        _log = log;
    }

    // ─── Entry point ───────────────────────────────────────────────────────

    public List<ValidationIssue> Validate(PackageData data)
    {
        var issues = new List<ValidationIssue>();

        foreach (var fond in data.Fonds)
        {
            ValidateFond(fond, issues);

            foreach (var cf in fond.CaseFiles)
                ValidateCaseFile(cf, fond.FondCode, issues);

            var allDocs = fond.CaseFiles.SelectMany(cf => cf.Documents)
                                        .Concat(fond.LooseDocuments);
            foreach (var doc in allDocs)
                ValidateDocument(doc, issues);
        }

        if (data.PackageType == PackageType.DIP && data.DipXacThuc != null)
            ValidateDipXacThuc(data.DipXacThuc, issues);

        int errors   = issues.Count(i => i.Severity == "ERROR");
        int warnings = issues.Count(i => i.Severity == "WARNING");
        _log($"  Kết quả validate: {errors} lỗi | {warnings} cảnh báo");

        return issues;
    }

    // ─── Fond ─────────────────────────────────────────────────────────────

    private static void ValidateFond(FondModel fond, List<ValidationIssue> issues)
    {
        RequiredStr(fond.FondCode,   "PHONG", 0, "MaPhong", issues);
        RequiredStr(fond.FondName,   "PHONG", 0, "TenPhong", issues);
        RequiredStr(fond.FondHistory,"PHONG", 0, "LichSu", issues);
        RequiredStr(fond.DocumentDateFrom, "PHONG", 0, "ThoiGianBD", issues);

        if (!string.IsNullOrEmpty(fond.Status)
            && fond.Status != "mở" && fond.Status != "đóng")
        {
            Warn("PHONG", 0, $"TrangThai '{fond.Status}' không hợp lệ (mở/đóng)", issues);
        }
    }

    // ─── CaseFile ─────────────────────────────────────────────────────────

    private static void ValidateCaseFile(CaseFileModel cf, string fondCode,
                                         List<ValidationIssue> issues)
    {
        RequiredStr(cf.CaseFileCode,  "HOSO", 0, "MaHoSo", issues);
        RequiredStr(cf.CaseFileTitle, "HOSO", 0, "TieuDe", issues);
        RequiredStr(cf.RetentionPeriod, "HOSO", 0, "ThoiHanLuu", issues);
        RequiredDate(cf.DateFrom, "HOSO", 0, "ThoiGianBD", issues);
        RequiredDate(cf.DateTo,   "HOSO", 0, "ThoiGianKT", issues);
        ValidateAccessLevel(cf.AccessLevel, "HOSO", 0, issues);

        if (cf.PageCount <= 0)
            Warn("HOSO", 0, $"Hồ sơ '{cf.CaseFileCode}': SoTrang nên > 0", issues);
    }

    // ─── Document ─────────────────────────────────────────────────────────

    private void ValidateDocument(DocumentModel doc, List<ValidationIssue> issues)
    {
        var sheet = doc.Category switch
        {
            DocumentCategory.VanBanHanhChinh => "TAILIEU_VANBANHANHCHINH",
            DocumentCategory.AnhPhimAmBan    => "TAILIEU_ANHANH",
            DocumentCategory.GhiAmVideo      => "TAILIEU_GHIAM_VIDEO",
            _ => "TAILIEU"
        };

        RequiredStr(doc.ArchivalCode,   sheet, 0, "MaLuuTru", issues);
        RequiredStr(doc.FileAttachment, sheet, 0, "TepTin", issues);
        ValidateAccessLevel(doc.AccessLevel, sheet, 0, issues);

        // Resolve + check file
        if (!string.IsNullOrEmpty(doc.FileAttachment))
        {
            var sourcePath = string.IsNullOrEmpty(doc.Path)
                ? System.IO.Path.Combine(_baseFolder, doc.FileAttachment)
                : System.IO.Path.Combine(_baseFolder, doc.Path);

            doc.ResolvedSourcePath = sourcePath;

            if (!System.IO.File.Exists(sourcePath))
            {
                Error(sheet, 0,
                    $"File không tìm thấy: {sourcePath} (TepTin={doc.FileAttachment})",
                    issues);
            }
            else
            {
                var ext = System.IO.Path.GetExtension(sourcePath);
                var validExts = doc.Category switch
                {
                    DocumentCategory.VanBanHanhChinh => ValidExtVanBan,
                    DocumentCategory.AnhPhimAmBan    => ValidExtAnhPhim,
                    DocumentCategory.GhiAmVideo      => ValidExtGhiAmVideo,
                    _ => ValidExtVanBan
                };

                if (!validExts.Contains(ext))
                {
                    Warn(sheet, 0,
                        $"Extension '{ext}' có thể không hợp lệ cho loại {doc.Category}. " +
                        $"File: {doc.FileAttachment}",
                        issues);
                }
            }
        }

        // Type-specific
        if (doc is VanBanModel vb)
        {
            // Trưᤁng bắt buộc theo TT05 Điều 4 khoản 1 điểm c
            RequiredStr(vb.DocumentUID,      sheet, 0, "MăĐịnhDanh", issues);
            RequiredStr(vb.DocumentType,     sheet, 0, "TênLoạiTaiLieu", issues);
            RequiredStr(vb.IssuingAuthority, sheet, 0, "CoQuan", issues);
            RequiredStr(vb.Summary,          sheet, 0, "TrichYeu", issues);
            RequiredStr(vb.Language,         sheet, 0, "NgonNgu", issues);
            RequiredStr(vb.RetentionPeriod,  sheet, 0, "ThoiHanLuu", issues);
            RequiredDateField(vb.IssuedDate, sheet, 0, "NgayBanHanh", issues); // bắt buộc
            if (vb.PageCount <= 0)
                Error(sheet, 0, $"Tài liệu '{vb.ArchivalCode}': SốLượngTrang phải > 0 (bắt buộc)", issues);
        }
        else if (doc is AnhPhimModel ap)
        {
            // Trưᤁng bắt buộc theo TT05 Điều 4 khoản 1 điểm d
            RequiredStr(ap.RetentionPeriod, sheet, 0, "ThoiHanLuu", issues);
            RequiredStr(ap.MediaType,       sheet, 0, "PhanLoai (am ban goc/duong ban)", issues);
            RequiredStr(ap.ArchivalNumber,  sheet, 0, "SoLuuTru", issues);
            RequiredStr(ap.EventName,       sheet, 0, "TenSuKien", issues);
            RequiredStr(ap.Title,           sheet, 0, "TieuDe", issues);
            RequiredStr(ap.Author,          sheet, 0, "TacGia", issues);
            RequiredStr(ap.Location,        sheet, 0, "DiaDiem", issues);
            RequiredStr(ap.ColorType,       sheet, 0, "MauSac", issues);
            RequiredStr(ap.Size,            sheet, 0, "CoPhin", issues);
            RequiredDateField(ap.CaptureDate, sheet, 0, "ThoiGianChup", issues); // bắt buộc
        }
        else if (doc is GhiAmVideoModel ga)
        {
            // Trưᤁng bắt buộc theo TT05 Điều 4 khoản 1 điểm đ
            RequiredStr(ga.RetentionPeriod, sheet, 0, "ThoiHanLuu", issues);
            RequiredStr(ga.MediaCategory,   sheet, 0, "PhanLoai (am thanh/video)", issues);
            RequiredStr(ga.ArchivalNumber,  sheet, 0, "SoLuuTru", issues);
            RequiredStr(ga.EventName,       sheet, 0, "TenSuKien", issues);
            RequiredStr(ga.Title,           sheet, 0, "TieuDe", issues);
            RequiredStr(ga.Author,          sheet, 0, "TacGia", issues);
            RequiredStr(ga.Location,        sheet, 0, "DiaDiem", issues);
            RequiredStr(ga.Language,        sheet, 0, "NgonNgu", issues);
            RequiredStr(ga.Duration,        sheet, 0, "ThoiLuong", issues);
            RequiredDateField(ga.RecordingDate, sheet, 0, "ThoiGianGhiAm", issues); // bắt buộc
        }
    }

    // ─── DIP ──────────────────────────────────────────────────────────────

    private static void ValidateDipXacThuc(DipXacThucModel dip, List<ValidationIssue> issues)
    {
        // Tất cả trưᤁng bắt buộc theo DIP.md phần 6.1
        RequiredStr(dip.MaXacThuc,         "DIP_XACTHUC", 2, "MaXacThuc",       issues);
        RequiredStr(dip.TenLuuTruLichSu,   "DIP_XACTHUC", 2, "TenLuuTru",       issues);
        RequiredStr(dip.HoTenNguoiNhan,    "DIP_XACTHUC", 2, "HoTen",           issues);
        RequiredStr(dip.MaDinhDanhCongDan, "DIP_XACTHUC", 2, "MaDinhDanh",      issues);
        RequiredStr(dip.MaLuuTruGoc,       "DIP_XACTHUC", 2, "MaLuuTruGoc",     issues);
        RequiredStr(dip.TenLoaiTaiLieu,    "DIP_XACTHUC", 2, "TenLoaiTaiLieu",  issues);
        RequiredStr(dip.TrichYeu,          "DIP_XACTHUC", 2, "TrichYeu",        issues);
        RequiredStr(dip.MucDichSuDung,     "DIP_XACTHUC", 2, "MucDich",         issues);
        RequiredStr(dip.ThoiHanSuDung,     "DIP_XACTHUC", 2, "ThoiHanSuDung",   issues);
        RequiredDateField(dip.NgayCap,     "DIP_XACTHUC", 2, "NgayCap",         issues);
        if (dip.SoLuongBan <= 0)
            Error("DIP_XACTHUC", 2, "SoLuongBan phải > 0", issues);
    }

    // ─── Issue builders ───────────────────────────────────────────────────

    private static void RequiredStr(string value, string sheet, int row,
                                    string field, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            Error(sheet, row, $"Trường bắt buộc '{field}' đang bỏ trống", issues);
    }

    private static void RequiredDate(string value, string sheet, int row,
                                     string field, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value)) return; // optional — chỉ kiểm tra format khi có giá trị
        if (!System.DateTime.TryParse(value, out _))
            Error(sheet, row, $"'{field}' = '{value}' không phải định dạng ngày hợp lệ (YYYY-MM-DD)", issues);
    }

    /// <summary>Trưᤁng ngày bắt buộc: lỗi nếu rỗng VÀ lỗi nếu sai format.</summary>
    private static void RequiredDateField(string value, string sheet, int row,
                                          string field, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Error(sheet, row, $"Trưᤁng bắt buộc '{field}' đang bỏ trống", issues);
            return;
        }
        if (!System.DateTime.TryParse(value, out _))
            Error(sheet, row, $"'{field}' = '{value}' không phải định dạng ngày hợp lệ (YYYY-MM-DD)", issues);
    }

    private static void ValidateAccessLevel(string value, string sheet, int row,
                                            List<ValidationIssue> issues)
    {
        var valid = new[] { "Công khai", "Hạn chế", "Mật", "Tuyệt mật" };
        if (string.IsNullOrWhiteSpace(value))
            Error(sheet, row, "Trưᤁng bắt buộc 'MucDoTiepCan' đang bỏ trống", issues);
        else if (!valid.Contains(value))
            Error(sheet, row, $"MucDoTiepCan '{value}' không nằm trong danh sách chuẩn (Công khai/Hạn chế/Mật/Tuyệt mật)", issues);
    }

    private static void Error(string sheet, int row, string msg,
                              List<ValidationIssue> issues) =>
        issues.Add(new ValidationIssue { Severity = "ERROR", Sheet = sheet, Row = row, Message = msg });

    private static void Warn(string sheet, int row, string msg,
                             List<ValidationIssue> issues) =>
        issues.Add(new ValidationIssue { Severity = "WARNING", Sheet = sheet, Row = row, Message = msg });
}
