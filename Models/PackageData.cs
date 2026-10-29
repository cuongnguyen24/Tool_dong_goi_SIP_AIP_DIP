namespace AXE.PackagingTool.Models;

/// <summary>
/// Thông tin xác thực DIP (Điều 42 TT05/2025) — chỉ dùng cho gói DIP.
/// </summary>
public class DipXacThucModel
{
    public string MaXacThuc          { get; set; } = "";
    public string TenLuuTruLichSu    { get; set; } = "";
    public string HoTenNguoiNhan     { get; set; } = "";
    public string MaDinhDanhCongDan  { get; set; } = "";
    public string MaLuuTruGoc        { get; set; } = "";
    public string SoKyHieuGoc        { get; set; } = "";
    public string TenLoaiTaiLieu     { get; set; } = "";
    public string TrichYeu           { get; set; } = "";
    public string MucDichSuDung      { get; set; } = "";
    public string NgayCap            { get; set; } = "";
    public string ThoiHanSuDung      { get; set; } = "";
    public int    SoLuongBan         { get; set; }
}

/// <summary>
/// Toàn bộ dữ liệu đọc từ file Excel, đã được nhóm theo phông.
/// </summary>
public class PackageData
{
    public PackageType          PackageType  { get; set; }
    public List<FondModel>      Fonds        { get; set; } = new();
    public DipXacThucModel?     DipXacThuc   { get; set; }

    /// <summary>
    /// True khi ít nhất 1 phông có hồ sơ → chọn _hoso variant cho tên ZIP.
    /// </summary>
    public bool HasCaseFiles => Fonds.Any(f => f.HasCaseFiles);
}

/// <summary>Kết quả validate hoặc đóng gói.</summary>
public class ValidationIssue
{
    public string Severity { get; set; } = "ERROR";  // ERROR | WARNING | INFO
    public string Sheet    { get; set; } = "";
    public int    Row      { get; set; }
    public string Message  { get; set; } = "";

    public override string ToString() =>
        $"[{Severity}] Sheet \"{Sheet}\" dòng {Row}: {Message}";
}

public class PackagingResult
{
    public bool   Success       { get; set; }
    public string ErrorMessage  { get; set; } = "";

    public List<string>          OutputFiles { get; set; } = new();
    public List<ValidationIssue> Issues      { get; set; } = new();

    public long TotalBytes { get; set; }
    public int  TotalFiles { get; set; }

    public bool HasErrors    => Issues.Any(i => i.Severity == "ERROR");
    public bool HasWarnings  => Issues.Any(i => i.Severity == "WARNING");
}
