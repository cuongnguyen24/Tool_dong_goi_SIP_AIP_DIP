namespace AXE.PackagingTool.Models;

public enum PackageType
{
    AIP,
    SIP,
    DIP
}

public enum DocumentCategory
{
    VanBanHanhChinh,   // Văn bản hành chính
    AnhPhimAmBan,      // Ảnh / phim âm bản
    GhiAmVideo         // Ghi âm / ghi hình
}

public static class PackageTypeExtensions
{
    public static string ToDisplayName(this PackageType t) => t switch
    {
        PackageType.AIP => "AIP — Gói lưu trữ (Archival)",
        PackageType.SIP => "SIP — Gói nộp lưu trữ (Submission)",
        PackageType.DIP => "DIP — Gói cung cấp người dùng (Dissemination)",
        _ => t.ToString()
    };

    public static string ToZipPrefix(this PackageType t, bool hasCaseFiles) => t switch
    {
        PackageType.AIP => hasCaseFiles ? "AIP_hoso" : "AIP_tailieu",
        PackageType.SIP => hasCaseFiles ? "SIP_hoso" : "SIP_tailieu",
        PackageType.DIP => "DIP",
        _ => t.ToString()
    };
}
