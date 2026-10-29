namespace AXE.PackagingTool.Models;

/// <summary>
/// Base class cho tất cả loại tài liệu.
/// </summary>
public abstract class DocumentModel
{
    public string FondCode        { get; set; } = "";
    public string CaseFileCode    { get; set; } = "";   // trống nếu tài liệu rời lẻ
    public string ArchivalCode    { get; set; } = "";
    public string RetentionPeriod { get; set; } = "";
    public string AccessLevel     { get; set; } = "Công khai";
    public string BackupMode      { get; set; } = "";
    public string BackupStatus    { get; set; } = "";

    /// <summary>Tên file đích bên trong ZIP (data/).</summary>
    public string FileAttachment  { get; set; } = "";

    /// <summary>
    /// Đường dẫn TƯƠNG ĐỐI từ BaseFolder đến file gốc trên đĩa.
    /// Nếu trống → tìm FileAttachment ngay trong BaseFolder.
    /// </summary>
    public string Path            { get; set; } = "";

    public string Note            { get; set; } = "";

    // Runtime — gán khi resolve
    public string ResolvedSourcePath { get; set; } = "";
    public string Sha256             { get; set; } = "";
    public long   FileSize           { get; set; }

    public abstract DocumentCategory Category { get; }

    /// <summary>Tên entry trong ZIP: representations/rep1/data/{FileAttachment}</summary>
    public string ZipDataEntry => $"representations/rep1/data/{FileAttachment}";
}

// ─── Văn bản hành chính (Sheet TAILIEU_VANBANHANHCHINH) ────────────────────

public class VanBanModel : DocumentModel
{
    public string DocumentUID       { get; set; } = "";
    public string DocumentType      { get; set; } = "";
    public string DocumentNumber    { get; set; } = "";
    public string DocumentSymbol    { get; set; } = "";
    public string IssuedDate        { get; set; } = "";
    public string IssuingAuthority  { get; set; } = "";
    public string Summary           { get; set; } = "";
    public string Language          { get; set; } = "vi";
    public int    PageCount         { get; set; }
    public int    SheetCount        { get; set; }
    public string InformationSymbol { get; set; } = "";
    public string Keywords          { get; set; } = "";
    public string ReliabilityLevel  { get; set; } = "";
    public string HandwrittenAnnotation { get; set; } = "";
    public string PhysicalCondition { get; set; } = "";
    public string ProcessingWorkflow { get; set; } = "";

    public override DocumentCategory Category => DocumentCategory.VanBanHanhChinh;
}

// ─── Ảnh / phim âm bản (Sheet TAILIEU_ANHANH) ──────────────────────────────

public class AnhPhimModel : DocumentModel
{
    public string MediaType         { get; set; } = "âm bản gốc";  // âm bản gốc / dương bản
    public string ArchivalNumber    { get; set; } = "";
    public string InformationSymbol { get; set; } = "";
    public string EventName         { get; set; } = "";
    public string Title             { get; set; } = "";
    public string Author            { get; set; } = "";
    public string Location          { get; set; } = "";
    public string CaptureDate       { get; set; } = "";
    public string ColorType         { get; set; } = "";             // Màu / Đen trắng
    public string Size              { get; set; } = "";
    public string AttachedDocuments { get; set; } = "";
    public string PhysicalCondition { get; set; } = "";

    public override DocumentCategory Category => DocumentCategory.AnhPhimAmBan;
}

// ─── Ghi âm / ghi hình (Sheet TAILIEU_GHIAM_VIDEO) ─────────────────────────

public class GhiAmVideoModel : DocumentModel
{
    public string MediaCategory     { get; set; } = "âm thanh";    // âm thanh / video
    public string ArchivalNumber    { get; set; } = "";
    public string InformationSymbol { get; set; } = "";
    public string EventName         { get; set; } = "";
    public string Title             { get; set; } = "";
    public string Author            { get; set; } = "";
    public string Location          { get; set; } = "";
    public string RecordingDate     { get; set; } = "";
    public string Language          { get; set; } = "vi";
    public string Duration          { get; set; } = "";             // HH:MM:SS
    public string AttachedDocuments { get; set; } = "";
    public string QualityLevel      { get; set; } = "";
    public string PhysicalCondition { get; set; } = "";

    public override DocumentCategory Category => DocumentCategory.GhiAmVideo;
}
