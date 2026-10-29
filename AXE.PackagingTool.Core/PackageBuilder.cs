using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using AXE.PackagingTool.Models;

namespace AXE.PackagingTool.Core;

public class PackageBuilder
{
	private const int ChunkSize = 4194304;

	private const string SoftwareName = "AXE-SoHoa Packaging Tool";

	private const string SoftwareVersion = "1.0.0";

	private const string EAD3Ns = "http://ead3.archivists.org/schema/";

	private const string MetsNs = "http://www.loc.gov/METS/";

	private const string PremisNs = "http://www.loc.gov/premis/v3";

	private readonly IProgress<(int Percent, string Message)> _progress;

	private readonly Action<string> _log;

	public PackageBuilder(IProgress<(int Percent, string Message)> progress, Action<string> log)
	{
		_progress = progress;
		_log = log;
	}

	public async Task<string> BuildAsync(FondModel fond, PackageType pkgType, DipXacThucModel? dipInfo, string outputFolder, CancellationToken ct)
	{
		string text = Guid.NewGuid().ToString().ToUpperInvariant();
		bool hasCaseFiles = fond.HasCaseFiles;
		string objId = ((pkgType == PackageType.DIP) ? ("uuid:" + text) : ("urn:" + fond.FondCode + ":uuid:" + text));
		string value = DateTime.Now.ToString("yyyyMMdd");
		string value2 = pkgType.ToZipPrefix(hasCaseFiles);
		string zipName = ((pkgType == PackageType.DIP) ? $"DIP_{text}_{value}.zip" : $"{value2}_{fond.FondCode}_{value}_001.zip");
		string zipPath = Path.Combine(outputFolder, zipName);
		_log("\ud83d\udce6 Bắt đầu đóng gói: " + zipName);
		List<DocumentModel> allDocs = fond.CaseFiles.SelectMany((CaseFileModel cf) => cf.Documents).Concat(fond.LooseDocuments).ToList();
		_progress.Report((5, "Tính checksum file gốc..."));
		await ComputeSha256AllAsync(allDocs, ct);
		_progress.Report((20, "Sinh metadata XML..."));
		DateTime now = DateTime.Now;
		byte[] data = GenerateEadRoot(fond, pkgType, now);
		string eadSha = ComputeSha256(data);
		byte[] array = null;
		string premisSha = "";
		if (pkgType != PackageType.DIP)
		{
			array = GeneratePremisRoot(fond, allDocs, now);
			premisSha = ComputeSha256(array);
		}
		Dictionary<DocumentModel, (byte[], string)> dictionary = new Dictionary<DocumentModel, (byte[], string)>();
		foreach (DocumentModel item4 in allDocs)
		{
			byte[] array2 = GenerateEadDoc(item4, now);
			string item = ComputeSha256(array2);
			dictionary[item4] = (array2, item);
		}
		byte[] data2 = GenerateRep1Mets(fond, allDocs, dictionary, pkgType, now);
		string rep1MetsSha = ComputeSha256(data2);
		byte[] data3 = GenerateRootMets(objId, fond, pkgType, now, eadSha, premisSha, rep1MetsSha);
		_progress.Report((30, "Tạo file ZIP..."));
		Directory.CreateDirectory(outputFolder);
		string result;
		await using (FileStream zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
		{
			using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);
			string root = SafeFolderName(objId);
			WriteEntry(archive, root + "/METS.xml", data3);
			WriteEntry(archive, root + "/metadata/descriptive/EAD.xml", data);
			if (array != null)
			{
				WriteEntry(archive, root + "/metadata/preservation/PREMIS.xml", array);
			}
			WriteEntry(archive, root + "/representations/rep1/METS.xml", data2);
			if (array != null)
			{
				byte[] data4 = GeneratePremisRep1(allDocs, now);
				WriteEntry(archive, root + "/representations/rep1/metadata/preservation/PREMIS_rep1.xml", data4);
			}
			foreach (KeyValuePair<DocumentModel, (byte[], string)> item5 in dictionary)
			{
				item5.Deconstruct(out var key, out var value3);
				(byte[], string) tuple = value3;
				DocumentModel documentModel = key;
				byte[] item2 = tuple.Item1;
				string text2 = "EAD_" + SafeFileName(documentModel.ArchivalCode) + ".xml";
				WriteEntry(archive, root + "/representations/rep1/metadata/descriptive/" + text2, item2);
			}
			_progress.Report((50, "Ghi schemas..."));
			WriteSchemas(archive, root);
			_progress.Report((60, "Ghi file tài liệu..."));
			int total = allDocs.Count;
			int processed = 0;
			foreach (DocumentModel doc in allDocs)
			{
				ct.ThrowIfCancellationRequested();
				if (!File.Exists(doc.ResolvedSourcePath))
				{
					throw new FileNotFoundException($"File tài liệu không tìm thấy (đã khai báo trong METS): {doc.ResolvedSourcePath} (TepTin={doc.FileAttachment})");
				}
				ZipArchiveEntry zipArchiveEntry = archive.CreateEntry(root + "/representations/rep1/data/" + doc.FileAttachment, CompressionLevel.Optimal);
				await using Stream entryStream = zipArchiveEntry.Open();
				await using FileStream fileStream = new FileStream(doc.ResolvedSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4194304, useAsync: true);
				await fileStream.CopyToAsync(entryStream, 4194304, ct);
				processed++;
				int item3 = 60 + processed * 35 / Math.Max(total, 1);
				_progress.Report((item3, "Đóng gói: " + doc.FileAttachment));
			}
			_progress.Report((98, "Hoàn tất..."));
			_log("✅ Đóng gói xong: " + zipName);
			result = zipPath;
		}
		return result;
	}

	private static async Task ComputeSha256AllAsync(IEnumerable<DocumentModel> docs, CancellationToken ct)
	{
		foreach (DocumentModel doc in docs)
		{
			if (File.Exists(doc.ResolvedSourcePath))
			{
				await using FileStream fs = new FileStream(doc.ResolvedSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4194304, useAsync: true);
				doc.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
				doc.FileSize = new FileInfo(doc.ResolvedSourcePath).Length;
			}
		}
	}

	private static string ComputeSha256(byte[] data)
	{
		return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
	}

	private static byte[] GenerateRootMets(string objId, FondModel fond, PackageType pkgType, DateTime now, string eadSha256, string premisSha256, string rep1MetsSha256)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using XmlWriter xmlWriter = XmlWriter.Create(memoryStream, XmlSettings());
		xmlWriter.WriteStartDocument();
		xmlWriter.WriteStartElement("mets", "mets", "http://www.loc.gov/METS/");
		xmlWriter.WriteAttributeString("xmlns", "xlink", null, "http://www.w3.org/1999/xlink");
		xmlWriter.WriteAttributeString("xmlns", "csip", null, "https://DILCIS.eu/XML/METS/CSIPExtensionMETS");
		xmlWriter.WriteAttributeString("OBJID", objId);
		xmlWriter.WriteAttributeString("LABEL", fond.FondName);
		xmlWriter.WriteAttributeString("TYPE", "Other");
		xmlWriter.WriteAttributeString("csip", "CONTENTINFORMATIONTYPE", "https://DILCIS.eu/XML/METS/CSIPExtensionMETS", "MIXED");
		xmlWriter.WriteAttributeString("PROFILE", "https://earkcsip.dilcis.eu/profile/E-ARK-CSIP.xml");
		string text = Guid.NewGuid().ToString();
		string text2 = Guid.NewGuid().ToString();
		string text3 = Guid.NewGuid().ToString();
		xmlWriter.WriteStartElement("mets", "metsHdr", null);
		xmlWriter.WriteAttributeString("CREATEDATE", now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
		xmlWriter.WriteAttributeString("RECORDSTATUS", "NEW");
		xmlWriter.WriteAttributeString("csip", "OAISPACKAGETYPE", "https://DILCIS.eu/XML/METS/CSIPExtensionMETS", pkgType.ToString());
		xmlWriter.WriteStartElement("mets", "agent", null);
		xmlWriter.WriteAttributeString("ROLE", "CREATOR");
		xmlWriter.WriteAttributeString("TYPE", "OTHER");
		xmlWriter.WriteAttributeString("OTHERTYPE", "SOFTWARE");
		xmlWriter.WriteElementString("mets", "name", null, "AXE-SoHoa Packaging Tool");
		xmlWriter.WriteStartElement("mets", "note", null);
		xmlWriter.WriteAttributeString("csip", "NOTETYPE", "https://DILCIS.eu/XML/METS/CSIPExtensionMETS", "SOFTWARE VERSION");
		xmlWriter.WriteString("1.0.0");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement("mets", "dmdSec", null);
		xmlWriter.WriteAttributeString("ID", "uuid-" + text);
		xmlWriter.WriteAttributeString("CREATED", now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
		WriteMdRef(xmlWriter, "metadata/descriptive/EAD.xml", "EAD", "3.0", eadSha256);
		xmlWriter.WriteEndElement();
		if (pkgType != PackageType.DIP && !string.IsNullOrEmpty(premisSha256))
		{
			xmlWriter.WriteStartElement("mets", "amdSec", null);
			xmlWriter.WriteStartElement("mets", "digiprovMD", null);
			xmlWriter.WriteAttributeString("ID", "uuid-" + text2);
			WriteMdRef(xmlWriter, "metadata/preservation/PREMIS.xml", "PREMIS", "3.0", premisSha256);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
		xmlWriter.WriteStartElement("mets", "fileSec", null);
		xmlWriter.WriteStartElement("mets", "fileGrp", null);
		xmlWriter.WriteAttributeString("USE", "Common Specification representations");
		xmlWriter.WriteStartElement("mets", "file", null);
		xmlWriter.WriteAttributeString("ID", "uuid-" + text3);
		xmlWriter.WriteAttributeString("MIMETYPE", "text/xml");
		xmlWriter.WriteAttributeString("CHECKSUM", rep1MetsSha256);
		xmlWriter.WriteAttributeString("CHECKSUMTYPE", "SHA-256");
		xmlWriter.WriteStartElement("mets", "FLocat", null);
		xmlWriter.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
		xmlWriter.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "representations/rep1/METS.xml");
		xmlWriter.WriteAttributeString("LOCTYPE", "URL");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement("mets", "structMap", null);
		xmlWriter.WriteAttributeString("TYPE", "PHYSICAL");
		xmlWriter.WriteAttributeString("ID", "uuid-" + Guid.NewGuid());
		xmlWriter.WriteAttributeString("LABEL", "CSIP");
		xmlWriter.WriteStartElement("mets", "div", null);
		xmlWriter.WriteAttributeString("ID", "uuid-" + Guid.NewGuid());
		xmlWriter.WriteAttributeString("LABEL", fond.FondCode);
		xmlWriter.WriteStartElement("mets", "div", null);
		xmlWriter.WriteAttributeString("ID", "uuid-" + Guid.NewGuid());
		xmlWriter.WriteAttributeString("LABEL", "representations");
		xmlWriter.WriteStartElement("mets", "mptr", null);
		xmlWriter.WriteAttributeString("LOCTYPE", "URL");
		xmlWriter.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
		xmlWriter.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "representations/rep1/METS.xml");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.Flush();
		return memoryStream.ToArray();
	}

	private static byte[] GenerateRep1Mets(FondModel fond, IList<DocumentModel> allDocs, Dictionary<DocumentModel, (byte[] Bytes, string Sha256)> docEadMap, PackageType pkgType, DateTime now)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using XmlWriter xmlWriter = XmlWriter.Create(memoryStream, XmlSettings());
		xmlWriter.WriteStartDocument();
		xmlWriter.WriteStartElement("mets", "mets", "http://www.loc.gov/METS/");
		xmlWriter.WriteAttributeString("xmlns", "xlink", null, "http://www.w3.org/1999/xlink");
		xmlWriter.WriteAttributeString("xmlns", "csip", null, "https://DILCIS.eu/XML/METS/CSIPExtensionMETS");
		xmlWriter.WriteAttributeString("LABEL", "rep1");
		xmlWriter.WriteStartElement("mets", "metsHdr", null);
		xmlWriter.WriteAttributeString("CREATEDATE", now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement("mets", "fileSec", null);
		xmlWriter.WriteStartElement("mets", "fileGrp", null);
		xmlWriter.WriteAttributeString("USE", "data");
		foreach (DocumentModel allDoc in allDocs)
		{
			xmlWriter.WriteStartElement("mets", "file", null);
			xmlWriter.WriteAttributeString("ID", $"uuid-{Guid.NewGuid()}");
			xmlWriter.WriteAttributeString("MIMETYPE", GuessMime(allDoc.FileAttachment));
			xmlWriter.WriteAttributeString("SIZE", allDoc.FileSize.ToString());
			xmlWriter.WriteAttributeString("CHECKSUM", allDoc.Sha256);
			xmlWriter.WriteAttributeString("CHECKSUMTYPE", "SHA-256");
			xmlWriter.WriteStartElement("mets", "FLocat", null);
			xmlWriter.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
			xmlWriter.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "data/" + allDoc.FileAttachment);
			xmlWriter.WriteAttributeString("LOCTYPE", "URL");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.Flush();
		return memoryStream.ToArray();
	}

	private static byte[] GenerateEadRoot(FondModel fond, PackageType pkgType, DateTime now)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using XmlWriter xmlWriter = XmlWriter.Create(memoryStream, XmlSettings());
		xmlWriter.WriteStartDocument();
		xmlWriter.WriteStartElement(null, "ead", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
		xmlWriter.WriteStartElement(null, "control", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteStartElement(null, "recordid", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteString(fond.FondCode);
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "filedesc", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteStartElement(null, "titlestmt", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteStartElement(null, "titleproper", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteString(fond.FondName);
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "maintenancestatus", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteAttributeString("value", "new");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "maintenanceagency", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteStartElement(null, "agencyname", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteString("AXE-SoHoa Packaging Tool");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "languagedeclaration", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteStartElement(null, "language", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteAttributeString("langcode", fond.Language ?? "");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "archdesc", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteAttributeString("level", "fonds");
		xmlWriter.WriteStartElement(null, "did", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteStartElement(null, "unitid", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteString(fond.FondCode);
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "unittitle", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteString(fond.FondName);
		xmlWriter.WriteEndElement();
		if (!string.IsNullOrEmpty(fond.DocumentDateFrom))
		{
			xmlWriter.WriteStartElement(null, "unitdatestructured", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteStartElement(null, "daterange", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteStartElement(null, "fromdate", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteString(fond.DocumentDateFrom);
			xmlWriter.WriteEndElement();
			if (!string.IsNullOrEmpty(fond.DocumentDateTo))
			{
				xmlWriter.WriteStartElement(null, "todate", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(fond.DocumentDateTo);
				xmlWriter.WriteEndElement();
			}
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
		if (!string.IsNullOrEmpty(fond.Language))
		{
			xmlWriter.WriteStartElement(null, "langmaterial", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteString(fond.Language);
			xmlWriter.WriteEndElement();
		}
		xmlWriter.WriteEndElement();
		if (!string.IsNullOrEmpty(fond.FondHistory))
		{
			xmlWriter.WriteStartElement(null, "bioghist", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteStartElement(null, "p", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteString(fond.FondHistory);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
		if (!string.IsNullOrEmpty(fond.ClassificationScheme))
		{
			xmlWriter.WriteStartElement(null, "arrangement", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteStartElement(null, "p", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteString(fond.ClassificationScheme);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
		if (fond.CaseFiles.Count > 0)
		{
			xmlWriter.WriteStartElement(null, "dsc", "http://ead3.archivists.org/schema/");
			foreach (CaseFileModel caseFile in fond.CaseFiles)
			{
				WriteCaseFileEad(xmlWriter, caseFile);
			}
			xmlWriter.WriteEndElement();
		}
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.Flush();
		return memoryStream.ToArray();
	}

	private static void WriteCaseFileEad(XmlWriter xw, CaseFileModel cf)
	{
		xw.WriteStartElement(null, "c", "http://ead3.archivists.org/schema/");
		xw.WriteAttributeString("level", "file");
		xw.WriteAttributeString("id", cf.CaseFileCode);
		xw.WriteStartElement(null, "did", "http://ead3.archivists.org/schema/");
		xw.WriteStartElement(null, "unitid", "http://ead3.archivists.org/schema/");
		xw.WriteString(cf.CaseFileCode);
		xw.WriteEndElement();
		xw.WriteStartElement(null, "unittitle", "http://ead3.archivists.org/schema/");
		xw.WriteString(cf.CaseFileTitle);
		xw.WriteEndElement();
		if (!string.IsNullOrEmpty(cf.DateFrom))
		{
			xw.WriteStartElement(null, "unitdatestructured", "http://ead3.archivists.org/schema/");
			xw.WriteStartElement(null, "daterange", "http://ead3.archivists.org/schema/");
			xw.WriteStartElement(null, "fromdate", "http://ead3.archivists.org/schema/");
			xw.WriteString(cf.DateFrom);
			xw.WriteEndElement();
			if (!string.IsNullOrEmpty(cf.DateTo))
			{
				xw.WriteStartElement(null, "todate", "http://ead3.archivists.org/schema/");
				xw.WriteString(cf.DateTo);
				xw.WriteEndElement();
			}
			xw.WriteEndElement();
			xw.WriteEndElement();
		}
		xw.WriteStartElement(null, "physdesc", "http://ead3.archivists.org/schema/");
		xw.WriteStartElement(null, "extent", "http://ead3.archivists.org/schema/");
		xw.WriteString($"{cf.PageCount} trang");
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteEndElement();
		if (!string.IsNullOrEmpty(cf.RetentionPeriod))
		{
			xw.WriteStartElement(null, "acqinfo", "http://ead3.archivists.org/schema/");
			xw.WriteStartElement(null, "p", "http://ead3.archivists.org/schema/");
			xw.WriteString("Thời hạn lưu: " + cf.RetentionPeriod);
			xw.WriteEndElement();
			xw.WriteEndElement();
		}
		if (!string.IsNullOrEmpty(cf.AccessLevel))
		{
			xw.WriteStartElement(null, "accessrestrict", "http://ead3.archivists.org/schema/");
			xw.WriteStartElement(null, "p", "http://ead3.archivists.org/schema/");
			xw.WriteString(cf.AccessLevel);
			xw.WriteEndElement();
			xw.WriteEndElement();
		}
		xw.WriteEndElement();
	}

	private static byte[] GenerateEadDoc(DocumentModel doc, DateTime now)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using XmlWriter xmlWriter = XmlWriter.Create(memoryStream, XmlSettings());
		xmlWriter.WriteStartDocument();
		xmlWriter.WriteStartElement(null, "ead", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteStartElement(null, "control", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteStartElement(null, "recordid", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteString(doc.ArchivalCode);
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "maintenancestatus", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteAttributeString("value", "new");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "maintenanceagency", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteStartElement(null, "agencyname", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteString("AXE-SoHoa Packaging Tool");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "archdesc", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteAttributeString("level", "item");
		xmlWriter.WriteStartElement(null, "did", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteStartElement(null, "unitid", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteString(doc.ArchivalCode);
		xmlWriter.WriteEndElement();
		if (doc is VanBanModel vanBanModel)
		{
			xmlWriter.WriteStartElement(null, "unittitle", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteString(vanBanModel.Summary);
			xmlWriter.WriteEndElement();
			if (!string.IsNullOrEmpty(vanBanModel.IssuedDate))
			{
				xmlWriter.WriteStartElement(null, "unitdate", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(vanBanModel.IssuedDate);
				xmlWriter.WriteEndElement();
			}
			if (!string.IsNullOrEmpty(vanBanModel.IssuingAuthority))
			{
				xmlWriter.WriteStartElement(null, "origination", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteStartElement(null, "corpname", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(vanBanModel.IssuingAuthority);
				xmlWriter.WriteEndElement();
				xmlWriter.WriteEndElement();
			}
			xmlWriter.WriteStartElement(null, "physdesc", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteStartElement(null, "extent", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteString($"{vanBanModel.PageCount} trang");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			if (!string.IsNullOrEmpty(vanBanModel.Language))
			{
				xmlWriter.WriteStartElement(null, "langmaterial", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(vanBanModel.Language);
				xmlWriter.WriteEndElement();
			}
			if (!string.IsNullOrEmpty(vanBanModel.DocumentType))
			{
				xmlWriter.WriteStartElement(null, "genreform", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(vanBanModel.DocumentType);
				xmlWriter.WriteEndElement();
			}
			if (!string.IsNullOrEmpty(vanBanModel.RetentionPeriod))
			{
				xmlWriter.WriteStartElement(null, "appraisal", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteStartElement(null, "p", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString("Thời hạn lưu: " + vanBanModel.RetentionPeriod);
				xmlWriter.WriteEndElement();
				xmlWriter.WriteEndElement();
			}
		}
		else if (doc is AnhPhimModel anhPhimModel)
		{
			xmlWriter.WriteStartElement(null, "unittitle", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteString(anhPhimModel.Title);
			xmlWriter.WriteEndElement();
			if (!string.IsNullOrEmpty(anhPhimModel.CaptureDate))
			{
				xmlWriter.WriteStartElement(null, "unitdate", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(anhPhimModel.CaptureDate);
				xmlWriter.WriteEndElement();
			}
			if (!string.IsNullOrEmpty(anhPhimModel.Author))
			{
				xmlWriter.WriteStartElement(null, "origination", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteStartElement(null, "persname", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(anhPhimModel.Author);
				xmlWriter.WriteEndElement();
				xmlWriter.WriteEndElement();
			}
			if (!string.IsNullOrEmpty(anhPhimModel.EventName))
			{
				xmlWriter.WriteStartElement(null, "scopecontent", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteStartElement(null, "p", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(anhPhimModel.EventName);
				xmlWriter.WriteEndElement();
				xmlWriter.WriteEndElement();
			}
			if (!string.IsNullOrEmpty(anhPhimModel.RetentionPeriod))
			{
				xmlWriter.WriteStartElement(null, "appraisal", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteStartElement(null, "p", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString("Thời hạn lưu: " + anhPhimModel.RetentionPeriod);
				xmlWriter.WriteEndElement();
				xmlWriter.WriteEndElement();
			}
		}
		else if (doc is GhiAmVideoModel ghiAmVideoModel)
		{
			xmlWriter.WriteStartElement(null, "unittitle", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteString(ghiAmVideoModel.Title);
			xmlWriter.WriteEndElement();
			if (!string.IsNullOrEmpty(ghiAmVideoModel.RecordingDate))
			{
				xmlWriter.WriteStartElement(null, "unitdate", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(ghiAmVideoModel.RecordingDate);
				xmlWriter.WriteEndElement();
			}
			if (!string.IsNullOrEmpty(ghiAmVideoModel.Language))
			{
				xmlWriter.WriteStartElement(null, "langmaterial", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(ghiAmVideoModel.Language);
				xmlWriter.WriteEndElement();
			}
			if (!string.IsNullOrEmpty(ghiAmVideoModel.EventName))
			{
				xmlWriter.WriteStartElement(null, "scopecontent", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteStartElement(null, "p", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString(ghiAmVideoModel.EventName);
				xmlWriter.WriteEndElement();
				xmlWriter.WriteEndElement();
			}
			if (!string.IsNullOrEmpty(ghiAmVideoModel.RetentionPeriod))
			{
				xmlWriter.WriteStartElement(null, "appraisal", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteStartElement(null, "p", "http://ead3.archivists.org/schema/");
				xmlWriter.WriteString("Thời hạn lưu: " + ghiAmVideoModel.RetentionPeriod);
				xmlWriter.WriteEndElement();
				xmlWriter.WriteEndElement();
			}
		}
		xmlWriter.WriteStartElement(null, "dao", "http://ead3.archivists.org/schema/");
		xmlWriter.WriteAttributeString("href", doc.FileAttachment);
		xmlWriter.WriteAttributeString("actuate", "onRequest");
		xmlWriter.WriteAttributeString("show", "embed");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		if (!string.IsNullOrEmpty(doc.AccessLevel))
		{
			xmlWriter.WriteStartElement(null, "accessrestrict", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteStartElement(null, "p", "http://ead3.archivists.org/schema/");
			xmlWriter.WriteString(doc.AccessLevel);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.Flush();
		return memoryStream.ToArray();
	}

	private static byte[] GeneratePremisRep1(IList<DocumentModel> allDocs, DateTime now)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using XmlWriter xmlWriter = XmlWriter.Create(memoryStream, XmlSettings());
		xmlWriter.WriteStartDocument();
		xmlWriter.WriteStartElement(null, "premis", "http://www.loc.gov/premis/v3");
		xmlWriter.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
		xmlWriter.WriteAttributeString("version", "3.0");
		xmlWriter.WriteStartElement(null, "event", "http://www.loc.gov/premis/v3");
		xmlWriter.WriteStartElement(null, "eventIdentifier", "http://www.loc.gov/premis/v3");
		xmlWriter.WriteStartElement(null, "eventIdentifierType", "http://www.loc.gov/premis/v3");
		xmlWriter.WriteString("UUID");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "eventIdentifierValue", "http://www.loc.gov/premis/v3");
		xmlWriter.WriteString(Guid.NewGuid().ToString());
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "eventType", "http://www.loc.gov/premis/v3");
		xmlWriter.WriteString("ingestion");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "eventDateTime", "http://www.loc.gov/premis/v3");
		xmlWriter.WriteString(now.ToString("yyyy-MM-ddTHH:mm:ss"));
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement(null, "eventOutcomeInformation", "http://www.loc.gov/premis/v3");
		xmlWriter.WriteStartElement(null, "eventOutcome", "http://www.loc.gov/premis/v3");
		xmlWriter.WriteString("success");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		foreach (DocumentModel item in allDocs.Where((DocumentModel d) => !string.IsNullOrEmpty(d.Sha256)))
		{
			xmlWriter.WriteStartElement(null, "object", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteAttributeString("xsi", "type", "http://www.w3.org/2001/XMLSchema-instance", "file");
			xmlWriter.WriteStartElement(null, "objectIdentifier", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteStartElement(null, "objectIdentifierType", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteString("local");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement(null, "objectIdentifierValue", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteString(item.FileAttachment);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement(null, "objectCharacteristics", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteStartElement(null, "compositionLevel", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteString("0");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement(null, "fixity", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteStartElement(null, "messageDigestAlgorithm", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteString("SHA-256");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement(null, "messageDigest", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteString(item.Sha256);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement(null, "size", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteString(item.FileSize.ToString());
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement(null, "format", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteStartElement(null, "formatDesignation", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteStartElement(null, "formatName", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteString(GuessMime(item.FileAttachment));
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement(null, "originalName", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteString(item.FileAttachment);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
		xmlWriter.WriteEndElement();
		xmlWriter.Flush();
		return memoryStream.ToArray();
	}

	private static byte[] GeneratePremisRoot(FondModel fond, IList<DocumentModel> allDocs, DateTime now)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using XmlWriter xmlWriter = XmlWriter.Create(memoryStream, XmlSettings());
		xmlWriter.WriteStartDocument();
		xmlWriter.WriteStartElement("premis", "http://www.loc.gov/premis/v3");
		xmlWriter.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
		xmlWriter.WriteAttributeString("version", "3.0");
		foreach (DocumentModel allDoc in allDocs)
		{
			xmlWriter.WriteStartElement("object");
			xmlWriter.WriteAttributeString("xsi", "type", "http://www.w3.org/2001/XMLSchema-instance", "file");
			xmlWriter.WriteStartElement("objectIdentifier");
			xmlWriter.WriteStartElement("objectIdentifierType");
			xmlWriter.WriteString("UUID");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement("objectIdentifierValue");
			xmlWriter.WriteString(Guid.NewGuid().ToString());
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement("objectCharacteristics");
			xmlWriter.WriteStartElement("compositionLevel");
			xmlWriter.WriteString("0");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement("fixity");
			xmlWriter.WriteStartElement("messageDigestAlgorithm");
			xmlWriter.WriteString("SHA-256");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement("messageDigest");
			xmlWriter.WriteString(allDoc.Sha256);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement("size");
			xmlWriter.WriteString(allDoc.FileSize.ToString());
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement("format");
			xmlWriter.WriteStartElement("formatDesignation");
			xmlWriter.WriteStartElement("formatName");
			xmlWriter.WriteString(GuessMime(allDoc.FileAttachment));
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement("originalName");
			xmlWriter.WriteString(allDoc.FileAttachment);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
		xmlWriter.WriteEndElement();
		xmlWriter.Flush();
		return memoryStream.ToArray();
	}

	private static void WriteSchemas(ZipArchive archive, string root)
	{
		Assembly executingAssembly = Assembly.GetExecutingAssembly();
		string[] array = new string[5] { "METS.xsd", "EAD.xsd", "EAD_doc.xsd", "EAD_pic.xsd", "EAD_media.xsd" };
		foreach (string text in array)
		{
			string name = "AXE.PackagingTool.Schemas." + text;
			using Stream stream = executingAssembly.GetManifestResourceStream(name);
			if (stream != null)
			{
				using Stream destination = archive.CreateEntry(root + "/schemas/" + text, CompressionLevel.Fastest).Open();
				stream.CopyTo(destination);
			}
		}
	}

	private static void WriteEntry(ZipArchive archive, string entryPath, byte[] data)
	{
		using Stream stream = archive.CreateEntry(entryPath, CompressionLevel.Optimal).Open();
		stream.Write(data, 0, data.Length);
	}

	private static void WriteMdRef(XmlWriter xw, string href, string mdtype, string mdtypeVersion, string sha256)
	{
		xw.WriteStartElement("mets", "mdRef", null);
		xw.WriteAttributeString("LOCTYPE", "URL");
		xw.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
		xw.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", href);
		xw.WriteAttributeString("MDTYPE", mdtype);
		xw.WriteAttributeString("MDTYPEVERSION", mdtypeVersion);
		xw.WriteAttributeString("MIMETYPE", "text/xml");
		xw.WriteAttributeString("CHECKSUM", sha256);
		xw.WriteAttributeString("CHECKSUMTYPE", "SHA-256");
		xw.WriteEndElement();
	}

	private static XmlWriterSettings XmlSettings()
	{
		return new XmlWriterSettings
		{
			Indent = true,
			Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			Async = false,
			NewLineChars = "\n",
			ConformanceLevel = ConformanceLevel.Document
		};
	}

	private static string SafeFolderName(string objId)
	{
		return objId.Replace(":", "_").Replace("/", "_").Replace("\\", "_");
	}

	private static string SafeFileName(string s)
	{
		return string.Concat(s.Select((char c) => (!Path.GetInvalidFileNameChars().Contains(c)) ? c : '_'));
	}

	private static string GuessMime(string filename)
	{
		string text = Path.GetExtension(filename).ToLowerInvariant();
		if (text != null)
		{
			int length = text.Length;
			if (length != 4)
			{
				if (length == 5)
				{
					char c = text[1];
					if (c != 'j')
					{
						if (c == 't' && text == ".tiff")
						{
							goto IL_01a1;
						}
					}
					else if (text == ".jpeg")
					{
						goto IL_0191;
					}
				}
			}
			else
			{
				switch (text[3])
				{
				case 'f':
					break;
				case 'g':
					goto IL_00c4;
				case '3':
					if (!(text == ".mp3"))
					{
						goto IL_01e1;
					}
					return "audio/mpeg";
				case 'a':
					if (!(text == ".wma"))
					{
						goto IL_01e1;
					}
					return "audio/x-ms-wma";
				case '4':
					if (!(text == ".mp4"))
					{
						goto IL_01e1;
					}
					return "video/mp4";
				case 'i':
					if (!(text == ".avi"))
					{
						goto IL_01e1;
					}
					return "video/x-msvideo";
				case 'v':
					goto IL_013d;
				case 'l':
					if (!(text == ".xml"))
					{
						goto IL_01e1;
					}
					return "text/xml";
				default:
					goto IL_01e1;
				}
				if (text == ".pdf")
				{
					return "application/pdf";
				}
				if (text == ".tif")
				{
					goto IL_01a1;
				}
			}
		}
		goto IL_01e1;
		IL_013d:
		if (!(text == ".wmv"))
		{
			if (!(text == ".wav"))
			{
				goto IL_01e1;
			}
			return "audio/wav";
		}
		return "video/x-ms-wmv";
		IL_00c4:
		if (text == ".jpg")
		{
			goto IL_0191;
		}
		if (!(text == ".png"))
		{
			goto IL_01e1;
		}
		return "image/png";
		IL_0191:
		return "image/jpeg";
		IL_01a1:
		return "image/tiff";
		IL_01e1:
		return "application/octet-stream";
	}
}
