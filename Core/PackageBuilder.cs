using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using AXE.PackagingTool.Models;

namespace AXE.PackagingTool.Core;

/// <summary>
/// Tạo các file XML metadata (METS, EAD, PREMIS) và đóng gói thành ZIP streaming.
/// Không load toàn bộ nội dung vào RAM — xử lý từng file theo chunk 4 MB.
/// </summary>
public class PackageBuilder
{
    private const int ChunkSize = 4 * 1024 * 1024; // 4 MB
    private const string SoftwareName    = "AXE-SoHoa Packaging Tool";
    private const string SoftwareVersion = "1.0.0";
    private const string EAD3Ns  = "http://ead3.archivists.org/schema/";
    private const string MetsNs  = "http://www.loc.gov/METS/";
    private const string PremisNs = "http://www.loc.gov/premis/v3";

    private readonly IProgress<(int Percent, string Message)> _progress;
    private readonly Action<string> _log;

    public PackageBuilder(
        IProgress<(int Percent, string Message)> progress,
        Action<string> log)
    {
        _progress = progress;
        _log      = log;
    }

    // ─── Public: Build một gói từ 1 FondModel ────────────────────────────

    public async Task<string> BuildAsync(
        FondModel fond,
        PackageType pkgType,
        DipXacThucModel? dipInfo,
        string outputFolder,
        CancellationToken ct)
    {
        string uuid     = Guid.NewGuid().ToString().ToUpperInvariant();
        bool hasCaseFiles = fond.HasCaseFiles;

        // OBJID theo chuẩn E-ARK
        string objId = pkgType == PackageType.DIP
            ? $"uuid:{uuid}"
            : $"urn:{fond.FondCode}:uuid:{uuid}";

        // Tên file ZIP đầu ra
        string date    = DateTime.Now.ToString("yyyyMMdd");
        string prefix  = pkgType.ToZipPrefix(hasCaseFiles);
        string zipName = pkgType == PackageType.DIP
            ? $"DIP_{uuid}_{date}.zip"
            : $"{prefix}_{fond.FondCode}_{date}_001.zip";

        string zipPath = Path.Combine(outputFolder, zipName);

        _log($"📦 Bắt đầu đóng gói: {zipName}");

        // Lấy danh sách tất cả tài liệu
        var allDocs = fond.CaseFiles.SelectMany(cf => cf.Documents)
                                    .Concat(fond.LooseDocuments)
                                    .ToList();

        // ── Bước 1: Tính SHA-256 tất cả file gốc ─────────────────────────
        _progress.Report((5, "Tính checksum file gốc..."));
        await ComputeSha256AllAsync(allDocs, ct);

        // ── Bước 2: Sinh XML vào MemoryStream (cần SHA-256 trước) ─────────
        _progress.Report((20, "Sinh metadata XML..."));

        var now = DateTime.Now;

        // EAD root
        byte[] eadRootBytes  = GenerateEadRoot(fond, pkgType, now);
        string eadRootSha256 = ComputeSha256(eadRootBytes);

        // PREMIS root (chỉ AIP/SIP)
        byte[]? premisRootBytes  = null;
        string  premisRootSha256 = "";
        if (pkgType != PackageType.DIP)
        {
            premisRootBytes  = GeneratePremisRoot(fond, allDocs, now);
            premisRootSha256 = ComputeSha256(premisRootBytes);
        }

        // Per-document EAD files
        var docEadMap = new Dictionary<DocumentModel, (byte[] Bytes, string Sha256)>();
        foreach (var doc in allDocs)
        {
            byte[] docEad    = GenerateEadDoc(doc, now);
            string docSha256 = ComputeSha256(docEad);
            docEadMap[doc]   = (docEad, docSha256);
        }

        // rep1/METS.xml
        byte[] rep1MetsBytes  = GenerateRep1Mets(fond, allDocs, docEadMap, pkgType, now);
        string rep1MetsSha256 = ComputeSha256(rep1MetsBytes);

        // Root METS.xml
        byte[] rootMetsBytes = GenerateRootMets(
            objId, fond, pkgType, now,
            eadRootSha256, premisRootSha256, rep1MetsSha256);

        // ── Bước 3: ZIP streaming ─────────────────────────────────────────
        _progress.Report((30, "Tạo file ZIP..."));

        Directory.CreateDirectory(outputFolder);
        await using var zipStream  = new FileStream(zipPath, FileMode.Create,
                                                    FileAccess.Write, FileShare.None,
                                                    bufferSize: 65536, useAsync: true);
        // leaveOpen: true — zipStream đã có await using bên ngoài, tránh double-dispose (B1)
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);

        string root = SafeFolderName(objId);

        // Root METS.xml
        WriteEntry(archive, $"{root}/METS.xml", rootMetsBytes);

        // metadata/descriptive/EAD.xml
        WriteEntry(archive, $"{root}/metadata/descriptive/EAD.xml", eadRootBytes);

        // metadata/preservation/PREMIS.xml (AIP/SIP only)
        if (premisRootBytes != null)
            WriteEntry(archive, $"{root}/metadata/preservation/PREMIS.xml", premisRootBytes);

        // representations/rep1/METS.xml
        WriteEntry(archive, $"{root}/representations/rep1/METS.xml", rep1MetsBytes);

        // representations/rep1/metadata/preservation/PREMIS_rep1.xml (AIP/SIP) — B6
        if (premisRootBytes != null)
        {
            byte[] premisRep1 = GeneratePremisRep1(allDocs, now);
            WriteEntry(archive, $"{root}/representations/rep1/metadata/preservation/PREMIS_rep1.xml", premisRep1);
        }

        // Per-document EAD files
        foreach (var (doc, (eadBytes, _)) in docEadMap)
        {
            string eadName = $"EAD_{SafeFileName(doc.ArchivalCode)}.xml";
            WriteEntry(archive, $"{root}/representations/rep1/metadata/descriptive/{eadName}", eadBytes);
        }

        // Schemas (embedded resources)
        _progress.Report((50, "Ghi schemas..."));
        WriteSchemas(archive, root);

        // Data files (streaming)
        _progress.Report((60, "Ghi file tài liệu..."));
        int total     = allDocs.Count;
        int processed = 0;

        foreach (var doc in allDocs)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(doc.ResolvedSourcePath))
            {
                // B5: METS đã liệt kê file này — không thể bỏ qua silently, throw để giữ tính nhất quán
                throw new FileNotFoundException(
                    $"File tài liệu không tìm thấy (đã khai báo trong METS): {doc.ResolvedSourcePath}" +
                    $" (TepTin={doc.FileAttachment})");
            }

            var entry = archive.CreateEntry($"{root}/representations/rep1/data/{doc.FileAttachment}",
                                            CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var fileStream  = new FileStream(
                doc.ResolvedSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: ChunkSize, useAsync: true);

            await fileStream.CopyToAsync(entryStream, ChunkSize, ct);

            processed++;
            int pct = 60 + (processed * 35 / Math.Max(total, 1));
            _progress.Report((pct, $"Đóng gói: {doc.FileAttachment}"));
        }

        _progress.Report((98, "Hoàn tất..."));
        _log($"✅ Đóng gói xong: {zipName}");

        return zipPath;
    }

    // ─── SHA-256 helpers ──────────────────────────────────────────────────

    private static async Task ComputeSha256AllAsync(
        IEnumerable<DocumentModel> docs, CancellationToken ct)
    {
        foreach (var doc in docs)
        {
            if (!File.Exists(doc.ResolvedSourcePath)) continue;
            await using var fs = new FileStream(
                doc.ResolvedSourcePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, ChunkSize, useAsync: true);
            byte[] hash = await SHA256.HashDataAsync(fs, ct);
            doc.Sha256    = Convert.ToHexString(hash).ToLowerInvariant();
            doc.FileSize  = new FileInfo(doc.ResolvedSourcePath).Length;
        }
    }

    private static string ComputeSha256(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ─── Root METS.xml ────────────────────────────────────────────────────

    private static byte[] GenerateRootMets(
        string objId, FondModel fond, PackageType pkgType, DateTime now,
        string eadSha256, string premisSha256, string rep1MetsSha256)
    {
        using var ms  = new MemoryStream();
        using var xw  = XmlWriter.Create(ms, XmlSettings());

        xw.WriteStartDocument();
        xw.WriteStartElement("mets", "mets", "http://www.loc.gov/METS/");
        xw.WriteAttributeString("xmlns", "xlink", null, "http://www.w3.org/1999/xlink");
        xw.WriteAttributeString("xmlns", "csip",  null, "https://DILCIS.eu/XML/METS/CSIPExtensionMETS");
        xw.WriteAttributeString("OBJID",  objId);
        xw.WriteAttributeString("LABEL",  fond.FondName);
        xw.WriteAttributeString("TYPE",   "Other");
        xw.WriteAttributeString("csip", "CONTENTINFORMATIONTYPE", "https://DILCIS.eu/XML/METS/CSIPExtensionMETS", "MIXED");
        xw.WriteAttributeString("PROFILE","https://earkcsip.dilcis.eu/profile/E-ARK-CSIP.xml");

        // metsHdr
        string dmdUuid  = Guid.NewGuid().ToString();
        string amdUuid  = Guid.NewGuid().ToString();
        string fileUuid = Guid.NewGuid().ToString();

        xw.WriteStartElement("mets", "metsHdr", null);
        xw.WriteAttributeString("CREATEDATE",   now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
        xw.WriteAttributeString("RECORDSTATUS", "NEW");
        xw.WriteAttributeString("csip", "OAISPACKAGETYPE", "https://DILCIS.eu/XML/METS/CSIPExtensionMETS",
            pkgType.ToString());
        // agent
        xw.WriteStartElement("mets", "agent", null);
        xw.WriteAttributeString("ROLE",      "CREATOR");
        xw.WriteAttributeString("TYPE",      "OTHER");
        xw.WriteAttributeString("OTHERTYPE", "SOFTWARE");
        xw.WriteElementString("mets", "name", null, SoftwareName);
        xw.WriteStartElement("mets", "note", null);
        xw.WriteAttributeString("csip", "NOTETYPE", "https://DILCIS.eu/XML/METS/CSIPExtensionMETS", "SOFTWARE VERSION");
        xw.WriteString(SoftwareVersion);
        xw.WriteEndElement();
        xw.WriteEndElement(); // agent
        xw.WriteEndElement(); // metsHdr

        // dmdSec
        xw.WriteStartElement("mets", "dmdSec", null);
        xw.WriteAttributeString("ID",      $"uuid-{dmdUuid}");
        xw.WriteAttributeString("CREATED", now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
        WriteMdRef(xw, "metadata/descriptive/EAD.xml", "EAD", "3.0", eadSha256);
        xw.WriteEndElement();

        // amdSec (AIP/SIP only)
        if (pkgType != PackageType.DIP && !string.IsNullOrEmpty(premisSha256))
        {
            xw.WriteStartElement("mets", "amdSec", null);
            xw.WriteStartElement("mets", "digiprovMD", null);
            xw.WriteAttributeString("ID", $"uuid-{amdUuid}");
            WriteMdRef(xw, "metadata/preservation/PREMIS.xml", "PREMIS", "3.0", premisSha256);
            xw.WriteEndElement();
            xw.WriteEndElement();
        }

        // fileSec
        xw.WriteStartElement("mets", "fileSec", null);
        xw.WriteStartElement("mets", "fileGrp", null);
        xw.WriteAttributeString("USE", "Common Specification representations");
        xw.WriteStartElement("mets", "file", null);
        xw.WriteAttributeString("ID",           $"uuid-{fileUuid}");
        xw.WriteAttributeString("MIMETYPE",     "text/xml");
        xw.WriteAttributeString("CHECKSUM",     rep1MetsSha256);
        xw.WriteAttributeString("CHECKSUMTYPE", "SHA-256");
        xw.WriteStartElement("mets", "FLocat", null);
        xw.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
        xw.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "representations/rep1/METS.xml");
        xw.WriteAttributeString("LOCTYPE", "URL");
        xw.WriteEndElement(); // FLocat
        xw.WriteEndElement(); // file
        xw.WriteEndElement(); // fileGrp
        xw.WriteEndElement(); // fileSec

        // structMap — BắT BUỘC theo E-ARK CSIP v2.0.4 (B4)
        xw.WriteStartElement("mets", "structMap", null);
        xw.WriteAttributeString("TYPE", "PHYSICAL");
        xw.WriteAttributeString("ID",   "uuid-" + Guid.NewGuid());
        xw.WriteAttributeString("LABEL", "CSIP");
        xw.WriteStartElement("mets", "div", null);
        xw.WriteAttributeString("ID",    "uuid-" + Guid.NewGuid());
        xw.WriteAttributeString("LABEL", fond.FondCode);
        xw.WriteStartElement("mets", "div", null);
        xw.WriteAttributeString("ID",    "uuid-" + Guid.NewGuid());
        xw.WriteAttributeString("LABEL", "representations");
        xw.WriteStartElement("mets", "mptr", null);
        xw.WriteAttributeString("LOCTYPE", "URL");
        xw.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
        xw.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "representations/rep1/METS.xml");
        xw.WriteEndElement(); // mptr
        xw.WriteEndElement(); // div representations
        xw.WriteEndElement(); // div root
        xw.WriteEndElement(); // structMap

        xw.WriteEndElement(); // mets
        xw.Flush();
        return ms.ToArray();
    }

    // ─── rep1/METS.xml ────────────────────────────────────────────────────

    private static byte[] GenerateRep1Mets(
        FondModel fond,
        IList<DocumentModel> allDocs,
        Dictionary<DocumentModel, (byte[] Bytes, string Sha256)> docEadMap,
        PackageType pkgType,
        DateTime now)
    {
        using var ms = new MemoryStream();
        using var xw = XmlWriter.Create(ms, XmlSettings());

        xw.WriteStartDocument();
        xw.WriteStartElement("mets", "mets", "http://www.loc.gov/METS/");
        xw.WriteAttributeString("xmlns", "xlink", null, "http://www.w3.org/1999/xlink");
        xw.WriteAttributeString("xmlns", "csip",  null, "https://DILCIS.eu/XML/METS/CSIPExtensionMETS");
        xw.WriteAttributeString("LABEL", "rep1");

        xw.WriteStartElement("mets", "metsHdr", null);
        xw.WriteAttributeString("CREATEDATE", now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
        xw.WriteEndElement();

        // fileSec — liệt kê tất cả file dữ liệu
        xw.WriteStartElement("mets", "fileSec", null);
        xw.WriteStartElement("mets", "fileGrp", null);
        xw.WriteAttributeString("USE", "data");

        foreach (var doc in allDocs)
        {
            xw.WriteStartElement("mets", "file", null);
            xw.WriteAttributeString("ID",           $"uuid-{Guid.NewGuid()}");
            xw.WriteAttributeString("MIMETYPE",     GuessMime(doc.FileAttachment));
            xw.WriteAttributeString("SIZE",         doc.FileSize.ToString());
            xw.WriteAttributeString("CHECKSUM",     doc.Sha256);
            xw.WriteAttributeString("CHECKSUMTYPE", "SHA-256");
            xw.WriteStartElement("mets", "FLocat", null);
            xw.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
            xw.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", $"data/{doc.FileAttachment}");
            xw.WriteAttributeString("LOCTYPE", "URL");
            xw.WriteEndElement();
            xw.WriteEndElement(); // file
        }

        xw.WriteEndElement(); // fileGrp
        xw.WriteEndElement(); // fileSec

        xw.WriteEndElement(); // mets
        xw.Flush();
        return ms.ToArray();
    }

    // ─── EAD.xml root (phông + hồ sơ) ────────────────────────────────────

    private static byte[] GenerateEadRoot(FondModel fond, PackageType pkgType, DateTime now)
    {
        using var ms = new MemoryStream();
        using var xw = XmlWriter.Create(ms, XmlSettings());

        xw.WriteStartDocument();
        // B2: default namespace → <ead xmlns="..."> thay vì <ead:ead xmlns:ead="...">
        xw.WriteStartElement(null, "ead", EAD3Ns);
        xw.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");

        // control
        xw.WriteStartElement(null, "control", EAD3Ns);
        xw.WriteStartElement(null, "recordid", EAD3Ns); xw.WriteString(fond.FondCode); xw.WriteEndElement();
        xw.WriteStartElement(null, "filedesc", EAD3Ns);
        xw.WriteStartElement(null, "titlestmt", EAD3Ns);
        xw.WriteStartElement(null, "titleproper", EAD3Ns); xw.WriteString(fond.FondName); xw.WriteEndElement();
        xw.WriteEndElement(); // titlestmt
        xw.WriteEndElement(); // filedesc
        xw.WriteStartElement(null, "maintenancestatus", EAD3Ns); xw.WriteAttributeString("value", "new"); xw.WriteEndElement();
        xw.WriteStartElement(null, "maintenanceagency", EAD3Ns);
        xw.WriteStartElement(null, "agencyname", EAD3Ns); xw.WriteString(SoftwareName); xw.WriteEndElement();
        xw.WriteEndElement();
        xw.WriteStartElement(null, "languagedeclaration", EAD3Ns);
        xw.WriteStartElement(null, "language", EAD3Ns); xw.WriteAttributeString("langcode", fond.Language ?? ""); xw.WriteEndElement();
        xw.WriteEndElement();
        xw.WriteEndElement(); // control

        // archdesc
        xw.WriteStartElement(null, "archdesc", EAD3Ns);
        xw.WriteAttributeString("level", "fonds");

        xw.WriteStartElement(null, "did", EAD3Ns);
        xw.WriteStartElement(null, "unitid", EAD3Ns); xw.WriteString(fond.FondCode); xw.WriteEndElement();
        xw.WriteStartElement(null, "unittitle", EAD3Ns); xw.WriteString(fond.FondName); xw.WriteEndElement();
        if (!string.IsNullOrEmpty(fond.DocumentDateFrom))
        {
            xw.WriteStartElement(null, "unitdatestructured", EAD3Ns);
            xw.WriteStartElement(null, "daterange", EAD3Ns);
            xw.WriteStartElement(null, "fromdate", EAD3Ns); xw.WriteString(fond.DocumentDateFrom); xw.WriteEndElement();
            if (!string.IsNullOrEmpty(fond.DocumentDateTo))
            { xw.WriteStartElement(null, "todate", EAD3Ns); xw.WriteString(fond.DocumentDateTo); xw.WriteEndElement(); }
            xw.WriteEndElement(); // daterange
            xw.WriteEndElement();
        }
        if (!string.IsNullOrEmpty(fond.Language))
        { xw.WriteStartElement(null, "langmaterial", EAD3Ns); xw.WriteString(fond.Language); xw.WriteEndElement(); }
        xw.WriteEndElement(); // did

        if (!string.IsNullOrEmpty(fond.FondHistory))
        { xw.WriteStartElement(null, "bioghist", EAD3Ns); xw.WriteStartElement(null, "p", EAD3Ns); xw.WriteString(fond.FondHistory); xw.WriteEndElement(); xw.WriteEndElement(); }

        if (!string.IsNullOrEmpty(fond.ClassificationScheme))
        { xw.WriteStartElement(null, "arrangement", EAD3Ns); xw.WriteStartElement(null, "p", EAD3Ns); xw.WriteString(fond.ClassificationScheme); xw.WriteEndElement(); xw.WriteEndElement(); }

        // Case files as dsc/c01
        if (fond.CaseFiles.Count > 0)
        {
            xw.WriteStartElement(null, "dsc", EAD3Ns);
            foreach (var cf in fond.CaseFiles)
                WriteCaseFileEad(xw, cf);
            xw.WriteEndElement();
        }

        xw.WriteEndElement(); // archdesc
        xw.WriteEndElement(); // ead
        xw.Flush();
        return ms.ToArray();
    }

    private static void WriteCaseFileEad(XmlWriter xw, CaseFileModel cf)
    {
        xw.WriteStartElement(null, "c", EAD3Ns);
        xw.WriteAttributeString("level", "file");
        xw.WriteAttributeString("id", cf.CaseFileCode);

        xw.WriteStartElement(null, "did", EAD3Ns);
        xw.WriteStartElement(null, "unitid", EAD3Ns); xw.WriteString(cf.CaseFileCode); xw.WriteEndElement();
        xw.WriteStartElement(null, "unittitle", EAD3Ns); xw.WriteString(cf.CaseFileTitle); xw.WriteEndElement();
        if (!string.IsNullOrEmpty(cf.DateFrom))
        {
            xw.WriteStartElement(null, "unitdatestructured", EAD3Ns);
            xw.WriteStartElement(null, "daterange", EAD3Ns);
            xw.WriteStartElement(null, "fromdate", EAD3Ns); xw.WriteString(cf.DateFrom); xw.WriteEndElement();
            if (!string.IsNullOrEmpty(cf.DateTo))
            { xw.WriteStartElement(null, "todate", EAD3Ns); xw.WriteString(cf.DateTo); xw.WriteEndElement(); }
            xw.WriteEndElement(); xw.WriteEndElement();
        }
        xw.WriteStartElement(null, "physdesc", EAD3Ns);
        xw.WriteStartElement(null, "extent", EAD3Ns); xw.WriteString($"{cf.PageCount} trang"); xw.WriteEndElement();
        xw.WriteEndElement();
        xw.WriteEndElement(); // did

        if (!string.IsNullOrEmpty(cf.RetentionPeriod))
        { xw.WriteStartElement(null, "acqinfo", EAD3Ns); xw.WriteStartElement(null, "p", EAD3Ns); xw.WriteString($"Thời hạn lưu: {cf.RetentionPeriod}"); xw.WriteEndElement(); xw.WriteEndElement(); }

        if (!string.IsNullOrEmpty(cf.AccessLevel))
        { xw.WriteStartElement(null, "accessrestrict", EAD3Ns); xw.WriteStartElement(null, "p", EAD3Ns); xw.WriteString(cf.AccessLevel); xw.WriteEndElement(); xw.WriteEndElement(); }

        xw.WriteEndElement(); // c
    }

    // ─── Per-document EAD ────────────────────────────────────────────────

    private static byte[] GenerateEadDoc(DocumentModel doc, DateTime now)
    {
        using var ms = new MemoryStream();
        using var xw = XmlWriter.Create(ms, XmlSettings());

        xw.WriteStartDocument();
        // B2: default namespace → <ead xmlns="..."> thay vì <ead:ead xmlns:ead="...">
        xw.WriteStartElement(null, "ead", EAD3Ns);

        xw.WriteStartElement(null, "control", EAD3Ns);
        xw.WriteStartElement(null, "recordid", EAD3Ns); xw.WriteString(doc.ArchivalCode); xw.WriteEndElement();
        xw.WriteStartElement(null, "maintenancestatus", EAD3Ns); xw.WriteAttributeString("value", "new"); xw.WriteEndElement();
        xw.WriteStartElement(null, "maintenanceagency", EAD3Ns);
        xw.WriteStartElement(null, "agencyname", EAD3Ns); xw.WriteString(SoftwareName); xw.WriteEndElement();
        xw.WriteEndElement();
        xw.WriteEndElement(); // control

        xw.WriteStartElement(null, "archdesc", EAD3Ns);
        xw.WriteAttributeString("level", "item");

        xw.WriteStartElement(null, "did", EAD3Ns);
        xw.WriteStartElement(null, "unitid", EAD3Ns); xw.WriteString(doc.ArchivalCode); xw.WriteEndElement();

        if (doc is VanBanModel vb)
        {
            xw.WriteStartElement(null, "unittitle", EAD3Ns); xw.WriteString(vb.Summary); xw.WriteEndElement();
            if (!string.IsNullOrEmpty(vb.IssuedDate))
            { xw.WriteStartElement(null, "unitdate", EAD3Ns); xw.WriteString(vb.IssuedDate); xw.WriteEndElement(); }
            if (!string.IsNullOrEmpty(vb.IssuingAuthority))
            { xw.WriteStartElement(null, "origination", EAD3Ns); xw.WriteStartElement(null, "corpname", EAD3Ns); xw.WriteString(vb.IssuingAuthority); xw.WriteEndElement(); xw.WriteEndElement(); }
            xw.WriteStartElement(null, "physdesc", EAD3Ns);
            xw.WriteStartElement(null, "extent", EAD3Ns); xw.WriteString($"{vb.PageCount} trang"); xw.WriteEndElement();
            xw.WriteEndElement();
            if (!string.IsNullOrEmpty(vb.Language))
            { xw.WriteStartElement(null, "langmaterial", EAD3Ns); xw.WriteString(vb.Language); xw.WriteEndElement(); }
            if (!string.IsNullOrEmpty(vb.DocumentType))
            { xw.WriteStartElement(null, "genreform", EAD3Ns); xw.WriteString(vb.DocumentType); xw.WriteEndElement(); }
            if (!string.IsNullOrEmpty(vb.RetentionPeriod))
            { xw.WriteStartElement(null, "appraisal", EAD3Ns); xw.WriteStartElement(null, "p", EAD3Ns); xw.WriteString($"Thời hạn lưu: {vb.RetentionPeriod}"); xw.WriteEndElement(); xw.WriteEndElement(); }
        }
        else if (doc is AnhPhimModel ap)
        {
            xw.WriteStartElement(null, "unittitle", EAD3Ns); xw.WriteString(ap.Title); xw.WriteEndElement();
            if (!string.IsNullOrEmpty(ap.CaptureDate))
            { xw.WriteStartElement(null, "unitdate", EAD3Ns); xw.WriteString(ap.CaptureDate); xw.WriteEndElement(); }
            if (!string.IsNullOrEmpty(ap.Author))
            { xw.WriteStartElement(null, "origination", EAD3Ns); xw.WriteStartElement(null, "persname", EAD3Ns); xw.WriteString(ap.Author); xw.WriteEndElement(); xw.WriteEndElement(); }
            if (!string.IsNullOrEmpty(ap.EventName))
            { xw.WriteStartElement(null, "scopecontent", EAD3Ns); xw.WriteStartElement(null, "p", EAD3Ns); xw.WriteString(ap.EventName); xw.WriteEndElement(); xw.WriteEndElement(); }
            if (!string.IsNullOrEmpty(ap.RetentionPeriod))
            { xw.WriteStartElement(null, "appraisal", EAD3Ns); xw.WriteStartElement(null, "p", EAD3Ns); xw.WriteString($"Thời hạn lưu: {ap.RetentionPeriod}"); xw.WriteEndElement(); xw.WriteEndElement(); }
        }
        else if (doc is GhiAmVideoModel ga)
        {
            xw.WriteStartElement(null, "unittitle", EAD3Ns); xw.WriteString(ga.Title); xw.WriteEndElement();
            if (!string.IsNullOrEmpty(ga.RecordingDate))
            { xw.WriteStartElement(null, "unitdate", EAD3Ns); xw.WriteString(ga.RecordingDate); xw.WriteEndElement(); }
            if (!string.IsNullOrEmpty(ga.Language))
            { xw.WriteStartElement(null, "langmaterial", EAD3Ns); xw.WriteString(ga.Language); xw.WriteEndElement(); }
            if (!string.IsNullOrEmpty(ga.EventName))
            { xw.WriteStartElement(null, "scopecontent", EAD3Ns); xw.WriteStartElement(null, "p", EAD3Ns); xw.WriteString(ga.EventName); xw.WriteEndElement(); xw.WriteEndElement(); }
            if (!string.IsNullOrEmpty(ga.RetentionPeriod))
            { xw.WriteStartElement(null, "appraisal", EAD3Ns); xw.WriteStartElement(null, "p", EAD3Ns); xw.WriteString($"Thời hạn lưu: {ga.RetentionPeriod}"); xw.WriteEndElement(); xw.WriteEndElement(); }
        }

        // DAO link
        xw.WriteStartElement(null, "dao", EAD3Ns);
        xw.WriteAttributeString("href", doc.FileAttachment);
        xw.WriteAttributeString("actuate", "onRequest");
        xw.WriteAttributeString("show", "embed");
        xw.WriteEndElement();

        xw.WriteEndElement(); // did

        // accessrestrict
        if (!string.IsNullOrEmpty(doc.AccessLevel))
        { xw.WriteStartElement(null, "accessrestrict", EAD3Ns); xw.WriteStartElement(null, "p", EAD3Ns); xw.WriteString(doc.AccessLevel); xw.WriteEndElement(); xw.WriteEndElement(); }

        xw.WriteEndElement(); // archdesc
        xw.WriteEndElement(); // ead
        xw.Flush();
        return ms.ToArray();
    }
    // ─── PREMIS_rep1.xml (B6) ─────────────────────────────────────────

    private static byte[] GeneratePremisRep1(IList<DocumentModel> allDocs, DateTime now)
    {
        using var ms = new MemoryStream();
        using var xw = XmlWriter.Create(ms, XmlSettings());

        xw.WriteStartDocument();
        xw.WriteStartElement(null, "premis", PremisNs);
        xw.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        xw.WriteAttributeString("version", "3.0");

        // Representation-level event: ingest
        xw.WriteStartElement(null, "event", PremisNs);
        xw.WriteStartElement(null, "eventIdentifier", PremisNs);
        xw.WriteStartElement(null, "eventIdentifierType", PremisNs); xw.WriteString("UUID"); xw.WriteEndElement();
        xw.WriteStartElement(null, "eventIdentifierValue", PremisNs); xw.WriteString(Guid.NewGuid().ToString()); xw.WriteEndElement();
        xw.WriteEndElement();
        xw.WriteStartElement(null, "eventType", PremisNs); xw.WriteString("ingestion"); xw.WriteEndElement();
        xw.WriteStartElement(null, "eventDateTime", PremisNs); xw.WriteString(now.ToString("yyyy-MM-ddTHH:mm:ss")); xw.WriteEndElement();
        xw.WriteStartElement(null, "eventOutcomeInformation", PremisNs);
        xw.WriteStartElement(null, "eventOutcome", PremisNs); xw.WriteString("success"); xw.WriteEndElement();
        xw.WriteEndElement();
        xw.WriteEndElement(); // event

        // Ối tượng cho mỗi file dữ liệu trong representation
        foreach (var doc in allDocs.Where(d => !string.IsNullOrEmpty(d.Sha256)))
        {
            xw.WriteStartElement(null, "object", PremisNs);
            xw.WriteAttributeString("xsi", "type", "http://www.w3.org/2001/XMLSchema-instance", "file");
            xw.WriteStartElement(null, "objectIdentifier", PremisNs);
            xw.WriteStartElement(null, "objectIdentifierType", PremisNs); xw.WriteString("local"); xw.WriteEndElement();
            xw.WriteStartElement(null, "objectIdentifierValue", PremisNs); xw.WriteString(doc.FileAttachment); xw.WriteEndElement();
            xw.WriteEndElement();
            xw.WriteStartElement(null, "objectCharacteristics", PremisNs);
            xw.WriteStartElement(null, "compositionLevel", PremisNs); xw.WriteString("0"); xw.WriteEndElement();
            xw.WriteStartElement(null, "fixity", PremisNs);
            xw.WriteStartElement(null, "messageDigestAlgorithm", PremisNs); xw.WriteString("SHA-256"); xw.WriteEndElement();
            xw.WriteStartElement(null, "messageDigest", PremisNs); xw.WriteString(doc.Sha256); xw.WriteEndElement();
            xw.WriteEndElement(); // fixity
            xw.WriteStartElement(null, "size", PremisNs); xw.WriteString(doc.FileSize.ToString()); xw.WriteEndElement();
            xw.WriteStartElement(null, "format", PremisNs);
            xw.WriteStartElement(null, "formatDesignation", PremisNs);
            xw.WriteStartElement(null, "formatName", PremisNs); xw.WriteString(GuessMime(doc.FileAttachment)); xw.WriteEndElement();
            xw.WriteEndElement(); xw.WriteEndElement(); // format
            xw.WriteEndElement(); // objectCharacteristics
            xw.WriteStartElement(null, "originalName", PremisNs); xw.WriteString(doc.FileAttachment); xw.WriteEndElement();
            xw.WriteEndElement(); // object
        }

        xw.WriteEndElement(); // premis
        xw.Flush();
        return ms.ToArray();
    }
    // ─── PREMIS.xml ───────────────────────────────────────────────────────

    private static byte[] GeneratePremisRoot(
        FondModel fond, IList<DocumentModel> allDocs, DateTime now)
    {
        using var ms = new MemoryStream();
        using var xw = XmlWriter.Create(ms, XmlSettings());

        xw.WriteStartDocument();
        xw.WriteStartElement("premis", "http://www.loc.gov/premis/v3");
        xw.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        xw.WriteAttributeString("version", "3.0");

        // Một object cho mỗi file dữ liệu
        foreach (var doc in allDocs)
        {
            xw.WriteStartElement("object");
            xw.WriteAttributeString("xsi", "type", "http://www.w3.org/2001/XMLSchema-instance", "file");

            xw.WriteStartElement("objectIdentifier");
            xw.WriteStartElement("objectIdentifierType"); xw.WriteString("UUID"); xw.WriteEndElement();
            xw.WriteStartElement("objectIdentifierValue"); xw.WriteString(Guid.NewGuid().ToString()); xw.WriteEndElement();
            xw.WriteEndElement();

            xw.WriteStartElement("objectCharacteristics");
            xw.WriteStartElement("compositionLevel"); xw.WriteString("0"); xw.WriteEndElement();
            xw.WriteStartElement("fixity");
            xw.WriteStartElement("messageDigestAlgorithm"); xw.WriteString("SHA-256"); xw.WriteEndElement();
            xw.WriteStartElement("messageDigest"); xw.WriteString(doc.Sha256); xw.WriteEndElement();
            xw.WriteEndElement(); // fixity
            xw.WriteStartElement("size"); xw.WriteString(doc.FileSize.ToString()); xw.WriteEndElement();
            xw.WriteStartElement("format");
            xw.WriteStartElement("formatDesignation");
            xw.WriteStartElement("formatName"); xw.WriteString(GuessMime(doc.FileAttachment)); xw.WriteEndElement();
            xw.WriteEndElement();
            xw.WriteEndElement(); // format
            xw.WriteEndElement(); // objectCharacteristics

            xw.WriteStartElement("originalName"); xw.WriteString(doc.FileAttachment); xw.WriteEndElement();

            xw.WriteEndElement(); // object
        }

        xw.WriteEndElement(); // premis
        xw.Flush();
        return ms.ToArray();
    }

    // ─── Schemas (embedded resources) ────────────────────────────────────

    private static void WriteSchemas(ZipArchive archive, string root)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var schemas  = new[] { "METS.xsd", "EAD.xsd", "EAD_doc.xsd", "EAD_pic.xsd", "EAD_media.xsd" };

        foreach (var schema in schemas)
        {
            var resName = $"AXE.PackagingTool.Schemas.{schema}";
            using var stream = assembly.GetManifestResourceStream(resName);
            if (stream == null) continue;

            var entry = archive.CreateEntry($"{root}/schemas/{schema}", CompressionLevel.Fastest);
            using var es = entry.Open();
            stream.CopyTo(es);
        }
    }

    // ─── Zip entry helper ─────────────────────────────────────────────────

    private static void WriteEntry(ZipArchive archive, string entryPath, byte[] data)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(data, 0, data.Length);
    }

    // ─── Misc helpers ─────────────────────────────────────────────────────

    private static void WriteMdRef(XmlWriter xw, string href, string mdtype,
                                   string mdtypeVersion, string sha256)
    {
        xw.WriteStartElement("mets", "mdRef", null);
        xw.WriteAttributeString("LOCTYPE",       "URL");
        xw.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
        xw.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", href);
        xw.WriteAttributeString("MDTYPE",        mdtype);
        xw.WriteAttributeString("MDTYPEVERSION", mdtypeVersion);
        xw.WriteAttributeString("MIMETYPE",      "text/xml");
        xw.WriteAttributeString("CHECKSUM",      sha256);
        xw.WriteAttributeString("CHECKSUMTYPE",  "SHA-256");
        xw.WriteEndElement();
    }

    private static XmlWriterSettings XmlSettings() => new()
    {
        Indent             = true,
        Encoding           = new UTF8Encoding(false), // BOM-less UTF-8
        Async              = false,
        NewLineChars       = "\n",
        ConformanceLevel   = ConformanceLevel.Document,
    };

    private static string SafeFolderName(string objId) =>
        objId.Replace(":", "_").Replace("/", "_").Replace("\\", "_");

    private static string SafeFileName(string s) =>
        string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static string GuessMime(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".pdf"  => "application/pdf",
            ".jpg"  or ".jpeg" => "image/jpeg",
            ".png"  => "image/png",
            ".tiff" or ".tif"  => "image/tiff",
            ".mp3"  => "audio/mpeg",
            ".wma"  => "audio/x-ms-wma",
            ".mp4"  => "video/mp4",
            ".avi"  => "video/x-msvideo",
            ".wmv"  => "video/x-ms-wmv",
            ".wav"  => "audio/wav",
            ".xml"  => "text/xml",
            _       => "application/octet-stream",
        };
    }
}
