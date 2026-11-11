using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Text.RegularExpressions;

namespace AXE.PackagingTool.Core;

internal static class PackageContentValidator
{
	private const string MetsNamespace = "http://www.loc.gov/METS/";
	private const string XlinkNamespace = "http://www.w3.org/1999/xlink";

	public static void Validate(ZipArchive archive, AXE.PackagingTool.Models.ProfileDTO profile, AXE.PackagingTool.Models.PackageType packageType, Action<string> log)
	{
		List<string> errors = new List<string>();
		Dictionary<string, ZipArchiveEntry> entries = archive.Entries.ToDictionary(e => Normalize(e.FullName), StringComparer.OrdinalIgnoreCase);
		Dictionary<string, XmlSchemaSet> compiledSchemas = new Dictionary<string, XmlSchemaSet>(StringComparer.OrdinalIgnoreCase);
		foreach (string requiredSchema in new[] { "schemas/METS.xsd", "schemas/xlink.xsd", "schemas/EAD.xsd" })
			if (!entries.ContainsKey(requiredSchema))
				errors.Add($"Thiếu schema bắt buộc trong ZIP: {requiredSchema}");
		foreach (string schema in entries.Keys.Where(p => p.StartsWith("schemas/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase)))
		{
			if (entries[schema].Length == 0)
				errors.Add($"Schema rỗng: {schema}");
			else
			{
				ValidateSchemaWellFormed(entries[schema], errors);
				ValidateSchemaDependencies(entries[schema], entries, errors);
			}
		}

		foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
		{
			try
			{
				XDocument xml;
				using (Stream stream = entry.Open())
				using (XmlReader reader = XmlReader.Create(stream, SecureReaderSettings()))
					xml = XDocument.Load(reader, LoadOptions.SetLineInfo);

				string path = Normalize(entry.FullName);
				string schemaName = GetSchemaName(path);
				if (schemaName != null)
					ValidateAgainstSchema(xml, entries, compiledSchemas, schemaName, path, errors);

				ValidateSchemaReferences(xml, path, entries, errors);
				if (path.EndsWith("EAD.xml", StringComparison.OrdinalIgnoreCase) || path.Contains("EAD_doc_", StringComparison.OrdinalIgnoreCase) || path.Contains("EAD_pic_", StringComparison.OrdinalIgnoreCase) || path.Contains("EAD_media_", StringComparison.OrdinalIgnoreCase))
				{
					ValidateMetadataFieldNames(xml, path, packageType, errors);
					ValidateMetadataCodes(xml, path, errors);
				}
				if (path.EndsWith("METS.xml", StringComparison.OrdinalIgnoreCase))
				{
					ValidateMetsReferences(xml, path, entries, errors);
					if (packageType == AXE.PackagingTool.Models.PackageType.AIP && path.Equals("METS.xml", StringComparison.OrdinalIgnoreCase))
						ValidateAipRootMets(xml, entries, path, errors);
					if (packageType == AXE.PackagingTool.Models.PackageType.AIP && path.Equals("representations/rep1/METS.xml", StringComparison.OrdinalIgnoreCase))
						ValidateAipRepresentationLinks(xml, profile, path, errors);
				}
			}
			catch (XmlException ex)
			{
				errors.Add($"XML không hợp lệ tại {entry.FullName}, dòng {ex.LineNumber}, vị trí {ex.LinePosition}: {ex.Message}");
			}
			catch (Exception ex)
			{
				errors.Add($"Không xác thực được {entry.FullName}: {ex.Message}");
			}
		}

		if (errors.Count > 0)
			throw new InvalidDataException("Gói không qua xác thực:" + Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", errors));

		log("✅ Đã xác thực XML, schema, tham chiếu METS và mã metadata.");
	}

	private static void ValidateSchemaWellFormed(ZipArchiveEntry schemaEntry, List<string> errors)
	{
		try
		{
			using Stream stream = schemaEntry.Open();
			XmlSchema? schema = XmlSchema.Read(XmlReader.Create(stream, SecureReaderSettings()), (sender, e) => errors.Add($"Schema {schemaEntry.FullName}: {e.Message}"));
			if (schema == null)
				errors.Add($"Không đọc được schema: {schemaEntry.FullName}");
		}
		catch (XmlException ex)
		{
			errors.Add($"XSD không hợp lệ tại {schemaEntry.FullName}, dòng {ex.LineNumber}: {ex.Message}");
		}
	}

	private static void ValidateSchemaDependencies(ZipArchiveEntry schemaEntry, Dictionary<string, ZipArchiveEntry> entries, List<string> errors)
	{
		try
		{
			using Stream stream = schemaEntry.Open();
			XDocument schema = XDocument.Load(XmlReader.Create(stream, SecureReaderSettings()));
			XNamespace xs = "http://www.w3.org/2001/XMLSchema";
			foreach (XAttribute location in schema.Root?.Elements().Where(e => e.Name == xs + "import" || e.Name == xs + "include" || e.Name == xs + "redefine").Attributes("schemaLocation") ?? Enumerable.Empty<XAttribute>())
			{
				if (Uri.TryCreate(location.Value, UriKind.Absolute, out Uri? absolute) && !absolute.IsFile)
					continue;
				CheckLocalReference(Normalize(schemaEntry.FullName), location.Value, entries, errors, "XSD schemaLocation");
			}
		}
		catch (XmlException ex)
		{
			errors.Add($"Không kiểm tra được phụ thuộc của schema {schemaEntry.FullName}, dòng {ex.LineNumber}: {ex.Message}");
		}
	}

	private static void ValidateAgainstSchema(XDocument document, Dictionary<string, ZipArchiveEntry> entries, Dictionary<string, XmlSchemaSet> compiledSchemas, string schemaName, string xmlPath, List<string> errors)
	{
		string schemaPath = "schemas/" + schemaName;
		if (!entries.TryGetValue(schemaPath, out ZipArchiveEntry? schemaEntry))
		{
			errors.Add($"Thiếu schema {schemaPath} để kiểm tra {xmlPath}.");
			return;
		}

		if (!compiledSchemas.TryGetValue(schemaName, out XmlSchemaSet? schemas))
		{
			schemas = new XmlSchemaSet { XmlResolver = null };
			using (Stream schemaStream = schemaEntry.Open())
			using (XmlReader schemaReader = XmlReader.Create(schemaStream, SecureReaderSettings()))
				schemas.Add(null, schemaReader);
			schemas.Compile();
			compiledSchemas[schemaName] = schemas;
		}

		document.Validate(schemas, (sender, e) =>
		{
			if (e.Severity == XmlSeverityType.Error)
				errors.Add($"Schema {schemaPath}, XML {xmlPath}: {e.Message}");
		}, addSchemaInfo: false);
	}

	private static void ValidateSchemaReferences(XDocument xml, string xmlPath, Dictionary<string, ZipArchiveEntry> entries, List<string> errors)
	{
		XElement? root = xml.Root;
		if (root == null)
			return;

		XAttribute? noNamespaceSchema = root.Attribute(XName.Get("noNamespaceSchemaLocation", "http://www.w3.org/2001/XMLSchema-instance"));
		if (noNamespaceSchema != null)
			CheckLocalReference(xmlPath, noNamespaceSchema.Value, entries, errors, "schemaLocation");

		XAttribute? schemaLocation = root.Attribute(XName.Get("schemaLocation", "http://www.w3.org/2001/XMLSchema-instance"));
		if (schemaLocation != null)
		{
			string[] parts = schemaLocation.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 1; i < parts.Length; i += 2)
				CheckLocalReference(xmlPath, parts[i], entries, errors, "schemaLocation");
		}

		foreach (XAttribute href in root.DescendantsAndSelf().Attributes(XName.Get("href", XlinkNamespace)))
			CheckLocalReference(xmlPath, href.Value, entries, errors, "xlink:href");
	}

	private static void ValidateMetsReferences(XDocument xml, string xmlPath, Dictionary<string, ZipArchiveEntry> entries, List<string> errors)
	{
		IEnumerable<XElement> elements = xml.Root?.DescendantsAndSelf() ?? Enumerable.Empty<XElement>();
		HashSet<string> ids = new HashSet<string>(elements.Attributes("ID").Select(a => a.Value), StringComparer.Ordinal);
		foreach (XAttribute reference in elements.Attributes("FILEID").Concat(elements.Attributes("DMDID")).Concat(elements.Attributes("ADMID")))
			foreach (string id in reference.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
				if (!ids.Contains(id))
					errors.Add($"{xmlPath}: tham chiếu {reference.Name}='{id}' không khớp ID nào trong METS.");
	}

	private static void ValidateAipRepresentationLinks(XDocument xml, AXE.PackagingTool.Models.ProfileDTO profile, string path, List<string> errors)
	{
		XNamespace mets = MetsNamespace;
		XNamespace xlink = XlinkNamespace;
		void Require(bool condition, string message)
		{
			if (!condition) errors.Add($"{path}: {message}");
		}

		XElement? root = xml.Root;
		if (root == null) return;
		Require(Regex.IsMatch((string?)root.Attribute("OBJID") ?? "", "^uuid-[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$", RegexOptions.CultureInvariant),
			"OBJID của bản đại diện phải theo dạng uuid-{UUID viết hoa}.");
		List<AXE.PackagingTool.Models.DocumentDTO> documents = profile.Documents
			.Where(d => d.IsPdfValid && !string.IsNullOrWhiteSpace(d.SourcePdfPath)).ToList();
		List<XElement> dmdSecs = root.Elements(mets + "dmdSec").ToList();
		List<XElement> metadataLinks = root.Descendants(mets + "div")
			.Where(d => (string?)d.Attribute("LABEL") == "MetadataLink/File").ToList();
		List<XElement> payloadFiles = root.Descendants(mets + "file")
			.Where(f => f.Parent?.Name == mets + "fileGrp" && (string?)f.Parent.Attribute("USE") == "Data").ToList();
		XElement? structMap = root.Elements(mets + "structMap").FirstOrDefault(m => (string?)m.Attribute("LABEL") == "CSIP");
		XElement? representationDiv = structMap?.Element(mets + "div");
		XElement? metadataDiv = representationDiv?.Elements(mets + "div").FirstOrDefault(d => (string?)d.Attribute("LABEL") == "Metadata");
		XElement? dataDiv = representationDiv?.Elements(mets + "div").FirstOrDefault(d => (string?)d.Attribute("LABEL") == "Data");
		XElement? metadataLinkDiv = representationDiv?.Elements(mets + "div").FirstOrDefault(d => (string?)d.Attribute("LABEL") == "MetadataLink");

		Require((string?)representationDiv?.Attribute("LABEL") == "rep1", "structMap[@LABEL='CSIP'] phải có div gốc LABEL=rep1.");
		Require(dmdSecs.Count == documents.Count, $"phải có một dmdSec cho mỗi tài liệu (cần {documents.Count}, hiện có {dmdSecs.Count}).");
		Require(metadataLinks.Count == documents.Count, $"phải có một div MetadataLink/File cho mỗi tài liệu (cần {documents.Count}, hiện có {metadataLinks.Count}).");
		Require(payloadFiles.Count == documents.Count, $"phải có một payload trong fileGrp USE=Data cho mỗi tài liệu (cần {documents.Count}, hiện có {payloadFiles.Count}).");
		Require(metadataDiv?.Element(mets + "fptr") != null, "thiếu fptr từ div Metadata đến mdRef PREMIS.");
		Require(dataDiv?.Element(mets + "fptr")?.Attribute("FILEID")?.Value == root.Descendants(mets + "fileGrp")
			.FirstOrDefault(g => (string?)g.Attribute("USE") == "Data")?.Attribute("ID")?.Value,
			"div Data phải trỏ đến fileGrp USE=Data.");

		foreach (XElement dmdSec in dmdSecs)
			Require(dmdSec.Elements(mets + "mdRef").Count() == 1, $"dmdSec '{(string?)dmdSec.Attribute("ID")}' phải chứa đúng một mdRef.");

		HashSet<string> amdIds = root.Elements(mets + "amdSec").Select(e => (string?)e.Attribute("ID"))
			.Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToHashSet(StringComparer.Ordinal);
		foreach (AXE.PackagingTool.Models.DocumentDTO document in documents)
		{
			string expectedMetadataPath = "metadata/descriptive/" + GetExpectedEadName(document);
			XElement? dmdSec = dmdSecs.SingleOrDefault(d =>
				(string?)d.Element(mets + "mdRef")?.Attribute(xlink + "href") == expectedMetadataPath);
			if (dmdSec == null)
			{
				Require(false, $"thiếu dmdSec riêng trỏ đến metadata '{expectedMetadataPath}'.");
				continue;
			}

			string? dmdId = (string?)dmdSec.Attribute("ID");
			XElement? link = metadataLinks.SingleOrDefault(d => (string?)d.Attribute("DMDID") == dmdId);
			Require(link != null, $"metadata '{expectedMetadataPath}' chưa được nối từ MetadataLink/File bằng DMDID.");
			if (link == null) continue;
			Require(amdIds.Contains((string?)link.Attribute("ADMID") ?? ""), $"MetadataLink/File của '{expectedMetadataPath}' thiếu ADMID hợp lệ đến amdSec.");
			string? fileId = (string?)link.Element(mets + "fptr")?.Attribute("FILEID");
			XElement? payload = payloadFiles.SingleOrDefault(f => (string?)f.Attribute("ID") == fileId);
			Require(payload != null, $"MetadataLink/File của '{expectedMetadataPath}' chưa nối đến payload bằng FILEID.");
			if (payload != null)
			{
				Require(Regex.IsMatch(fileId ?? "", "^ID-[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$", RegexOptions.CultureInvariant),
					$"FILEID '{fileId}' không theo dạng ID-{{UUID}}.");
				Require((string?)payload.Element(mets + "FLocat")?.Attribute(xlink + "href") == "data/" + document.SafeFileName,
					$"FILEID '{fileId}' không trỏ đến đúng payload '{document.SafeFileName}'.");
			}
		}
	}

	private static void ValidateAipRootMets(XDocument xml, Dictionary<string, ZipArchiveEntry> entries, string path, List<string> errors)
	{
		XNamespace mets = MetsNamespace;
		XNamespace xlink = XlinkNamespace;
		void Require(bool condition, string message)
		{
			if (!condition) errors.Add($"{path}: {message}");
		}

		XElement? root = xml.Root;
		if (root == null) return;
		XElement? schemaGroup = root.Descendants(mets + "fileGrp").FirstOrDefault(g => (string?)g.Attribute("USE") == "Schemas");
		XElement? representationGroup = root.Descendants(mets + "fileGrp").FirstOrDefault(g => (string?)g.Attribute("USE") == "Representations/rep1");
		XElement? representationFile = representationGroup?.Elements(mets + "file").SingleOrDefault(f =>
			(string?)f.Element(mets + "FLocat")?.Attribute(xlink + "href") == "representations/rep1/METS.xml");
		XElement? structRoot = root.Elements(mets + "structMap")
			.FirstOrDefault(m => (string?)m.Attribute("LABEL") == "CSIP")?.Element(mets + "div");
		XElement? schemasDiv = structRoot?.Elements(mets + "div").FirstOrDefault(d => (string?)d.Attribute("LABEL") == "Schemas");
		XElement? representationDiv = structRoot?.Elements(mets + "div").FirstOrDefault(d => (string?)d.Attribute("LABEL") == "Representations/rep1");
		XElement? mptr = representationDiv?.Element(mets + "mptr");

		Require(schemaGroup != null, "METS gói AIP phải có fileGrp USE=Schemas.");
		Require(schemaGroup?.Elements(mets + "file").Count() == entries.Keys.Count(p => p.StartsWith("schemas/", StringComparison.OrdinalIgnoreCase) && !p.EndsWith("/", StringComparison.Ordinal)),
			"fileGrp USE=Schemas phải lập danh mục tất cả schema trong gói.");
		Require(schemasDiv?.Element(mets + "fptr")?.Attribute("FILEID")?.Value == (string?)schemaGroup?.Attribute("ID"),
			"structMap div LABEL=Schemas phải trỏ đến fileGrp USE=Schemas.");
		Require(representationGroup != null, "METS gói AIP phải có fileGrp USE=Representations/rep1.");
		Require(representationFile != null, "fileGrp USE=Representations/rep1 phải tham chiếu METS của rep1.");
		if (representationFile != null)
		{
			foreach (string attribute in new[] { "ID", "MIMETYPE", "SIZE", "CREATED", "CHECKSUM", "CHECKSUMTYPE" })
				Require(representationFile.Attribute(attribute) != null, $"file METS rep1 thiếu @{attribute}.");
			Require(Regex.IsMatch((string?)representationFile.Attribute("ID") ?? "", "^ID-[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$", RegexOptions.CultureInvariant),
				"ID của file METS rep1 phải theo dạng ID-{UUID viết hoa}.");
		}
		Require(mptr != null && (string?)mptr.Attribute(xlink + "href") == "representations/rep1/METS.xml",
			"structMap div Representations/rep1 phải trỏ đến METS rep1.");
		Require(mptr != null && (string?)mptr.Attribute(xlink + "title") == (string?)representationGroup?.Attribute("ID"),
			"mptr/@xlink:title phải khớp ID của fileGrp USE=Representations/rep1.");
	}

	private static string GetExpectedEadName(AXE.PackagingTool.Models.DocumentDTO document)
	{
		string extension = Path.GetExtension(document.SafeFileName).ToLowerInvariant();
		string prefix = new[] { ".mp3", ".mp4", ".avi", ".wav", ".wmv", ".mov" }.Contains(extension) ? "EAD_media_" :
			new[] { ".jpg", ".jpeg", ".png", ".tiff", ".gif", ".bmp" }.Contains(extension) ? "EAD_pic_" : "EAD_doc_";
		return prefix + document.SafeFileName + ".xml";
	}

	private static void ValidateMetadataCodes(XDocument xml, string path, List<string> errors)
	{
		if (xml.Root == null)
			return;
		Dictionary<string, string> enums = new(StringComparer.OrdinalIgnoreCase)
		{
			["maintenance"] = "01,02,03,04,05,06,07",
			["mode"] = "01,02,03",
			["language"] = "01,02,03,04,05,06,07,08,09,10,11",
			["confidenceLevel"] = "01,02,03",
			["riskRecovery"] = "0,1",
			["riskRecoveryStatus"] = "01,02",
			["process"] = "0,1",
			["docAttached"] = "0,1",
			["typePic"] = "01,02",
			["typeMedia"] = "01,02",
			["colour"] = "01,02",
			["typeName"] = string.Join(',', Enumerable.Range(1, 32).Select(n => n.ToString("00", CultureInfo.InvariantCulture))),
			["purpose"] = "01,02,03",
			["feeObjectType"] = string.Join(',', Enumerable.Range(1, 10).Select(n => n.ToString("00", CultureInfo.InvariantCulture)))
		};
		foreach (KeyValuePair<string, string> item in enums)
		{
			string field = item.Key;
			HashSet<string> allowed = new HashSet<string>(item.Value.Split(','), StringComparer.OrdinalIgnoreCase);
			foreach (XElement element in xml.Root.Elements().Where(e => e.Name.LocalName.Equals(field, StringComparison.OrdinalIgnoreCase)))
				foreach (string code in element.Value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
					if (!allowed.Contains(code))
						errors.Add($"{path}: giá trị <{field}> '{code}' không hợp lệ; giá trị cho phép: {string.Join(", ", allowed)}.");
		}

		foreach (XElement date in xml.Root.Elements().Where(e => new[] { "startDate", "endDate", "issuedDate", "requestDate" }.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase)))
		{
			string value = date.Value.Trim();
			if (value.Length > 0 && !new[] { "yyyy-MM-dd", "dd/MM/yyyy", "MM/yyyy", "yyyy-MM", "yyyy" }.Any(format => DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
				errors.Add($"{path}: giá trị ngày <{date.Name.LocalName}> '{value}' không theo định dạng được chấp nhận.");
		}
	}

	private static void ValidateMetadataFieldNames(XDocument xml, string path, AXE.PackagingTool.Models.PackageType packageType, List<string> errors)
	{
		if (xml.Root == null)
			return;

		HashSet<string> allowed;
		if (path.Equals("metadata/descriptive/EAD.xml", StringComparison.OrdinalIgnoreCase))
		{
			allowed = packageType switch
			{
				AXE.PackagingTool.Models.PackageType.SIP => SplitFields("fileCode,title,maintenance,mode,language,startDate,endDate,keyword,totalDoc,numberOfPaper,numberOfPage,format,inforSign,confidenceLevel,paperFileCode,riskRecovery,riskRecoveryStatus,description"),
				AXE.PackagingTool.Models.PackageType.AIP => SplitFields("arcFileCode,title,maintenance,mode,language,startDate,endDate,keyword,totalDoc,numberOfPaper,numberOfPage,format,inforSign,confidenceLevel,paperFileCode,riskRecovery,riskRecoveryStatus,description"),
				_ => SplitFields("requestID,requestDate,purpose,purposeContent,feeObjectType,researchTopic,description")
			};
		}
		else
		{
			string identifierField = packageType == AXE.PackagingTool.Models.PackageType.SIP ? "docCode" : "arcDocCode";
			if (path.Contains("EAD_pic_", StringComparison.OrdinalIgnoreCase))
				allowed = SplitFields(identifierField + ",maintenance,typePic,archivesNumber,inforSign,eventName,imageTitle,photographer,photoPlace,photoTime,colour,filmSize,docAttached,mode,format,riskRecovery,riskRecoveryStatus,description");
			else if (path.Contains("EAD_media_", StringComparison.OrdinalIgnoreCase))
				allowed = SplitFields(identifierField + ",maintenance,typeMedia,archivesNumber,inforSign,eventName,movieTitle,recorder,recordPlace,recordDate,language,playTime,docAttached,mode,quality,format,riskRecovery,riskRecoveryStatus,description");
			else
				allowed = SplitFields("docId," + identifierField + ",maintenance,typeName,codeNumber,codeNotation,issuedDate,organName,subject,language,numberOfPage,inforSign,keyword,mode,confidenceLevel,autograph,format,process,riskRecovery,riskRecoveryStatus,description");
		}

		foreach (XElement field in xml.Root.Elements())
			if (!allowed.Contains(field.Name.LocalName))
				errors.Add($"{path}: tên trường <{field.Name.LocalName}> không thuộc cấu trúc metadata {packageType} theo Thông tư 05.");
	}

	private static HashSet<string> SplitFields(string fields) => new(fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

	private static string? GetSchemaName(string xmlPath)
	{
		if (xmlPath.Equals("METS.xml", StringComparison.OrdinalIgnoreCase) || xmlPath.Equals("representations/rep1/METS.xml", StringComparison.OrdinalIgnoreCase))
			return "METS.xsd";
		if (xmlPath.Equals("metadata/descriptive/EAD.xml", StringComparison.OrdinalIgnoreCase))
			return "EAD.xsd";
		if (xmlPath.Contains("EAD_media_", StringComparison.OrdinalIgnoreCase))
			return "EAD_media.xsd";
		if (xmlPath.Contains("EAD_pic_", StringComparison.OrdinalIgnoreCase))
			return "EAD_pic.xsd";
		if (xmlPath.Contains("EAD_doc_", StringComparison.OrdinalIgnoreCase))
			return "EAD_doc.xsd";
		return null;
	}

	private static void CheckLocalReference(string sourcePath, string reference, Dictionary<string, ZipArchiveEntry> entries, List<string> errors, string kind)
	{
		if (Uri.TryCreate(reference, UriKind.Absolute, out Uri? absolute) && !absolute.IsFile)
			return;
		try
		{
			Uri baseUri = new Uri("https://package.invalid/" + sourcePath, UriKind.Absolute);
			Uri resolved = new Uri(baseUri, reference);
			string target = Normalize(Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/')));
			if (!entries.ContainsKey(target))
				errors.Add($"{sourcePath}: {kind} trỏ tới tệp không có trong ZIP: '{reference}' (đường dẫn đã giải: '{target}').");
		}
		catch (UriFormatException)
		{
			errors.Add($"{sourcePath}: {kind} không phải URI hợp lệ: '{reference}'.");
		}
	}

	private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

	private static XmlReaderSettings SecureReaderSettings() => new XmlReaderSettings
	{
		DtdProcessing = DtdProcessing.Prohibit,
		XmlResolver = null,
		CloseInput = false
	};
}
