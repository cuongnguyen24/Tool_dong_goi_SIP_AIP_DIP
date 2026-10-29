using ClosedXML.Excel;
using System.IO;
using AXE.PackagingTool.Models;

namespace AXE.PackagingTool.Core;

/// <summary>
/// Đọc và parse file Excel .xlsx thành PackageData.
/// Cột được xác định theo tên header (row 1), không phụ thuộc vị trí cột.
/// </summary>
public class ExcelReader
{
    private readonly Action<string> _log;

    public ExcelReader(Action<string> log) => _log = log;

    // ─── Public entry point ────────────────────────────────────────────────

    public PackageData Read(string filePath, PackageType packageType)
    {
        _log($"📂 Đọc file Excel: {System.IO.Path.GetFileName(filePath)}");
        using var wb = new XLWorkbook(filePath);

        var fonds     = ReadFonds(wb);
        var caseFiles = ReadCaseFiles(wb);
        var vanBan    = ReadVanBan(wb);
        var anhPhim   = ReadAnhPhim(wb);
        var ghiAm     = ReadGhiAmVideo(wb);

        // ── Link documents → case files or fond (loose) ───────────────────
        var caseIndex = caseFiles.ToDictionary(
            cf => (cf.FondCode, cf.CaseFileCode), cf => cf);

        foreach (var doc in vanBan.Cast<DocumentModel>()
                     .Concat(anhPhim).Concat(ghiAm))
        {
            if (!string.IsNullOrWhiteSpace(doc.CaseFileCode)
                && caseIndex.TryGetValue((doc.FondCode, doc.CaseFileCode), out var cf))
            {
                cf.Documents.Add(doc);
            }
            else
            {
                var fond = fonds.FirstOrDefault(f => f.FondCode == doc.FondCode);
                fond?.LooseDocuments.Add(doc);
            }
        }

        // ── Link case files → fonds ────────────────────────────────────────
        var fondIndex = fonds.ToDictionary(f => f.FondCode);
        foreach (var cf in caseFiles)
        {
            if (fondIndex.TryGetValue(cf.FondCode, out var fond))
                fond.CaseFiles.Add(cf);
        }

        _log($"✅ Đọc xong: {fonds.Count} phông | {caseFiles.Count} hồ sơ " +
             $"| {vanBan.Count + anhPhim.Count + ghiAm.Count} tài liệu");

        var data = new PackageData { PackageType = packageType, Fonds = fonds };

        if (packageType == PackageType.DIP)
            data.DipXacThuc = ReadDipXacThuc(wb);

        return data;
    }

    // ─── Sheet: PHONG ─────────────────────────────────────────────────────

    private List<FondModel> ReadFonds(XLWorkbook wb)
    {
        const string sheet = "PHONG";
        if (!wb.TryGetWorksheet(sheet, out var ws))
            throw new InvalidDataException($"Không tìm thấy sheet '{sheet}' trong file Excel.");

        var map = BuildColumnMap(ws);
        var result = new List<FondModel>();

        foreach (var row in DataRows(ws))
        {
            result.Add(new FondModel
            {
                FondCode             = Str(ws, row, map, "MaPhong"),
                FondName             = Str(ws, row, map, "TenPhong"),
                Status               = Str(ws, row, map, "TrangThai"),
                FondHistory          = Str(ws, row, map, "LichSu"),
                DocumentDateFrom     = Str(ws, row, map, "ThoiGianBD"),
                DocumentDateTo       = Str(ws, row, map, "ThoiGianKT"),
                TotalDocuments       = Int(ws, row, map, "TongSo"),
                ClassificationScheme = Str(ws, row, map, "PhanLoai"),
                Language             = Str(ws, row, map, "NgonNgu"),
                BackupMode           = Str(ws, row, map, "CheDoDuPhong"),
                Note                 = Str(ws, row, map, "GhiChu"),
            });
        }

        _log($"  • PHONG: {result.Count} bản ghi");
        return result;
    }

    // ─── Sheet: HOSO ──────────────────────────────────────────────────────

    private List<CaseFileModel> ReadCaseFiles(XLWorkbook wb)
    {
        const string sheet = "HOSO";
        if (!wb.TryGetWorksheet(sheet, out var ws))
            return new List<CaseFileModel>(); // Không có HOSO → AIP_tailieu

        var map = BuildColumnMap(ws);
        var result = new List<CaseFileModel>();

        foreach (var row in DataRows(ws))
        {
            result.Add(new CaseFileModel
            {
                FondCode            = Str(ws, row, map, "MaPhong"),
                CaseFileCode        = Str(ws, row, map, "MaHoSo"),
                CaseFileTitle       = Str(ws, row, map, "TieuDe"),
                RetentionPeriod     = Str(ws, row, map, "ThoiHanLuu"),
                AccessLevel         = Str(ws, row, map, "MucDoTiepCan"),
                Language            = Str(ws, row, map, "NgonNgu"),
                DateFrom            = Str(ws, row, map, "ThoiGianBD"),
                DateTo              = Str(ws, row, map, "ThoiGianKT"),
                Keywords            = Str(ws, row, map, "TuKhoa"),
                TotalDocuments      = Int(ws, row, map, "TongSo"),
                SheetCount          = Int(ws, row, map, "SoTo"),
                PageCount           = Int(ws, row, map, "SoTrang"),
                PhysicalCondition   = Str(ws, row, map, "TinhTrangVatLy"),
                InformationSymbol   = Str(ws, row, map, "KyHieuThongTin"),
                ReliabilityLevel    = Str(ws, row, map, "MucDoTinCay"),
                OriginalPaperCode   = Str(ws, row, map, "MaGoc"),
                BackupMode          = Str(ws, row, map, "CheDoDuPhong"),
                BackupStatus        = Str(ws, row, map, "TinhTrangDuPhong"),
                CaseFileAttachment  = Str(ws, row, map, "TepTin"),
                Note                = Str(ws, row, map, "GhiChu"),
            });
        }

        _log($"  • HOSO: {result.Count} bản ghi");
        return result;
    }

    // ─── Sheet: TAILIEU_VANBANHANHCHINH ───────────────────────────────────

    private List<VanBanModel> ReadVanBan(XLWorkbook wb)
    {
        const string sheet = "TAILIEU_VANBANHANHCHINH";
        if (!wb.TryGetWorksheet(sheet, out var ws))
            return new List<VanBanModel>();

        var map = BuildColumnMap(ws);
        var result = new List<VanBanModel>();

        foreach (var row in DataRows(ws))
        {
            result.Add(new VanBanModel
            {
                FondCode            = Str(ws, row, map, "MaPhong"),
                CaseFileCode        = Str(ws, row, map, "MaHoSo"),
                DocumentUID         = Str(ws, row, map, "MaDinhDanh"),
                ArchivalCode        = Str(ws, row, map, "MaLuuTru"),
                RetentionPeriod     = Str(ws, row, map, "ThoiHanLuu"),
                DocumentType        = Str(ws, row, map, "LoaiTaiLieu"),
                DocumentNumber      = Str(ws, row, map, "So"),
                DocumentSymbol      = Str(ws, row, map, "KyHieu"),
                IssuedDate          = Str(ws, row, map, "NgayBanHanh"),
                IssuingAuthority    = Str(ws, row, map, "CoQuan"),
                Summary             = Str(ws, row, map, "TrichYeu"),
                Language            = Str(ws, row, map, "NgonNgu"),
                PageCount           = Int(ws, row, map, "SoTrang"),
                SheetCount          = Int(ws, row, map, "SoTo"),
                InformationSymbol   = Str(ws, row, map, "KyHieuThongTin"),
                Keywords            = Str(ws, row, map, "TuKhoa"),
                AccessLevel         = Str(ws, row, map, "MucDoTiepCan"),
                ReliabilityLevel    = Str(ws, row, map, "MucDoTinCay"),
                HandwrittenAnnotation = Str(ws, row, map, "ButTich"),
                PhysicalCondition   = Str(ws, row, map, "TinhTrangVatLy"),
                ProcessingWorkflow  = Str(ws, row, map, "QuyTrinh"),
                BackupMode          = Str(ws, row, map, "CheDoDuPhong"),
                BackupStatus        = Str(ws, row, map, "TinhTrangDuPhong"),
                FileAttachment      = Str(ws, row, map, "TepTin"),
                Path                = Str(ws, row, map, "Path"),
                Note                = Str(ws, row, map, "GhiChu"),
            });
        }

        _log($"  • TAILIEU_VANBANHANHCHINH: {result.Count} bản ghi");
        return result;
    }

    // ─── Sheet: TAILIEU_ANHANH ────────────────────────────────────────────

    private List<AnhPhimModel> ReadAnhPhim(XLWorkbook wb)
    {
        const string sheet = "TAILIEU_ANHANH";
        if (!wb.TryGetWorksheet(sheet, out var ws))
            return new List<AnhPhimModel>();

        var map = BuildColumnMap(ws);
        var result = new List<AnhPhimModel>();

        foreach (var row in DataRows(ws))
        {
            result.Add(new AnhPhimModel
            {
                FondCode            = Str(ws, row, map, "MaPhong"),
                CaseFileCode        = Str(ws, row, map, "MaHoSo"),
                ArchivalCode        = Str(ws, row, map, "MaLuuTru"),
                RetentionPeriod     = Str(ws, row, map, "ThoiHanLuu"),
                MediaType           = Str(ws, row, map, "PhanLoai"),
                ArchivalNumber      = Str(ws, row, map, "SoLuuTru"),
                InformationSymbol   = Str(ws, row, map, "KyHieuThongTin"),
                EventName           = Str(ws, row, map, "TenSuKien"),
                Title               = Str(ws, row, map, "TieuDe"),
                Author              = Str(ws, row, map, "TacGia"),
                Location            = Str(ws, row, map, "DiaDiem"),
                CaptureDate         = Str(ws, row, map, "ThoiGianChup"),
                ColorType           = Str(ws, row, map, "MauSac"),
                Size                = Str(ws, row, map, "Co"),
                AttachedDocuments   = Str(ws, row, map, "TaiLieuDiKem"),
                AccessLevel         = Str(ws, row, map, "MucDoTiepCan"),
                PhysicalCondition   = Str(ws, row, map, "TinhTrangVatLy"),
                BackupMode          = Str(ws, row, map, "CheDoDuPhong"),
                BackupStatus        = Str(ws, row, map, "TinhTrangDuPhong"),
                FileAttachment      = Str(ws, row, map, "TepTin"),
                Path                = Str(ws, row, map, "Path"),
                Note                = Str(ws, row, map, "GhiChu"),
            });
        }

        _log($"  • TAILIEU_ANHANH: {result.Count} bản ghi");
        return result;
    }

    // ─── Sheet: TAILIEU_GHIAM_VIDEO ───────────────────────────────────────

    private List<GhiAmVideoModel> ReadGhiAmVideo(XLWorkbook wb)
    {
        const string sheet = "TAILIEU_GHIAM_VIDEO";
        if (!wb.TryGetWorksheet(sheet, out var ws))
            return new List<GhiAmVideoModel>();

        var map = BuildColumnMap(ws);
        var result = new List<GhiAmVideoModel>();

        foreach (var row in DataRows(ws))
        {
            result.Add(new GhiAmVideoModel
            {
                FondCode            = Str(ws, row, map, "MaPhong"),
                CaseFileCode        = Str(ws, row, map, "MaHoSo"),
                ArchivalCode        = Str(ws, row, map, "MaLuuTru"),
                RetentionPeriod     = Str(ws, row, map, "ThoiHanLuu"),
                MediaCategory       = Str(ws, row, map, "PhanLoai"),
                ArchivalNumber      = Str(ws, row, map, "SoLuuTru"),
                InformationSymbol   = Str(ws, row, map, "KyHieuThongTin"),
                EventName           = Str(ws, row, map, "TenSuKien"),
                Title               = Str(ws, row, map, "TieuDe"),
                Author              = Str(ws, row, map, "TacGia"),
                Location            = Str(ws, row, map, "DiaDiem"),
                RecordingDate       = Str(ws, row, map, "ThoiGian"),
                Language            = Str(ws, row, map, "NgonNgu"),
                Duration            = Str(ws, row, map, "ThoiLuong"),
                AttachedDocuments   = Str(ws, row, map, "TaiLieuDiKem"),
                AccessLevel         = Str(ws, row, map, "MucDoTiepCan"),
                QualityLevel        = Str(ws, row, map, "ChatLuong"),
                PhysicalCondition   = Str(ws, row, map, "TinhTrangVatLy"),
                BackupMode          = Str(ws, row, map, "CheDoDuPhong"),
                BackupStatus        = Str(ws, row, map, "TinhTrangDuPhong"),
                FileAttachment      = Str(ws, row, map, "TepTin"),
                Path                = Str(ws, row, map, "Path"),
                Note                = Str(ws, row, map, "GhiChu"),
            });
        }

        _log($"  • TAILIEU_GHIAM_VIDEO: {result.Count} bản ghi");
        return result;
    }

    // ─── Sheet: DIP_XACTHUC (chỉ DIP) ────────────────────────────────────

    private DipXacThucModel? ReadDipXacThuc(XLWorkbook wb)
    {
        const string sheet = "DIP_XACTHUC";
        if (!wb.TryGetWorksheet(sheet, out var ws))
            return null;

        var map = BuildColumnMap(ws);
        var rows = DataRows(ws).ToList();
        if (rows.Count == 0) return null;

        int row = rows.First();
        return new DipXacThucModel
        {
            MaXacThuc         = Str(ws, row, map, "MaXacThuc"),
            TenLuuTruLichSu   = Str(ws, row, map, "TenLuuTru"),
            HoTenNguoiNhan    = Str(ws, row, map, "HoTen"),
            MaDinhDanhCongDan = Str(ws, row, map, "MaDinhDanh"),
            MaLuuTruGoc       = Str(ws, row, map, "MaLuuTruGoc"),
            SoKyHieuGoc       = Str(ws, row, map, "SoKyHieu"),
            TenLoaiTaiLieu    = Str(ws, row, map, "LoaiTaiLieu"),
            TrichYeu          = Str(ws, row, map, "TrichYeu"),
            MucDichSuDung     = Str(ws, row, map, "MucDich"),
            NgayCap           = Str(ws, row, map, "NgayCap"),
            ThoiHanSuDung     = Str(ws, row, map, "ThoiHan"),
            SoLuongBan        = Int(ws, row, map, "SoLuong"),
        };
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Build map: key (normalized) → column number từ header row 1.</summary>
    private static Dictionary<string, int> BuildColumnMap(IXLWorksheet ws)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 50;
        for (int col = 1; col <= lastCol; col++)
        {
            var header = ws.Cell(1, col).GetString().Trim();
            if (!string.IsNullOrEmpty(header))
            {
                // Normalize: bỏ dấu tiếng Việt, bỏ khoảng trắng, uppercase
                var key = NormalizeHeader(header);
                if (!map.ContainsKey(key))
                    map[key] = col;
            }
        }
        return map;
    }

    /// <summary>Normalize header: chỉ giữ ký tự ASCII + chữ số.</summary>
    private static string NormalizeHeader(string header) =>
        System.Text.RegularExpressions.Regex.Replace(
            header.ToUpperInvariant()
                  .Normalize(System.Text.NormalizationForm.FormD),
            @"[^A-Z0-9]", "");

    private static IEnumerable<int> DataRows(IXLWorksheet ws)
    {
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        // Dòng 1 = mã trường kỹ thuật (ExcelReader đọc để map cột)
        // Dòng 2 = tên tiếng Việt (chỉ để hiển thị cho người dùng)
        // Dữ liệu bắt đầu từ dòng 3
        for (int r = 3; r <= lastRow; r++)
        {
            if (!ws.Row(r).IsEmpty())
                yield return r;
        }
    }

    private static string Str(IXLWorksheet ws, int row,
                              Dictionary<string, int> map, string key)
    {
        if (!map.TryGetValue(key.ToUpperInvariant(), out int col))
            return "";
        return ws.Cell(row, col).GetString().Trim();
    }

    private static int Int(IXLWorksheet ws, int row,
                           Dictionary<string, int> map, string key)
    {
        if (!map.TryGetValue(key.ToUpperInvariant(), out int col))
            return 0;
        var val = ws.Cell(row, col).GetString().Trim();
        return int.TryParse(val, out var n) ? n : 0;
    }
}
