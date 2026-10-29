using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace AXE.PackagingTool.Core;

internal static class PackageContentValidator
{
	private const string MetsNamespace = "http://www.loc.gov/METS/";
	private const string XlinkNamespace = "http://www.w3.org/1999/xlink";

	public static void Validate(ZipArchive archive, AXE.PackagingTool.Models.ProfileDTO profile, Action<string> log)
	{
		List<string> errors = new List<string>();
		Dictionary<string, ZipArchiveEntry> entries = archive.Entries.ToDictionary(e => Normalize(e.FullName), StringComparer.OrdinalIgnoreCase);
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
					ValidateAgainstSchema(xml, entries, schemaName, path, errors);

				ValidateSchemaReferences(xml, path, entries, errors);
				if (path.EndsWith("EAD.xml", StringComparison.OrdinalIgnoreCase) || path.Contains("EAD_doc_", StringComparison.OrdinalIgnoreCase) || path.Contains("EAD_pic_", StringComparison.OrdinalIgnoreCase) || path.Contains("EAD_media_", StringComparison.OrdinalIgnoreCase))
					ValidateMetadataCodes(xml, path, errors);
				if (path.EndsWith("METS.xml", StringComparison.OrdinalIgnoreCase))
					ValidateMetsReferences(xml, path, entries, errors);
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

	private static void ValidateAgainstSchema(XDocument document, Dictionary<string, ZipArchiveEntry> entries, string schemaName, string xmlPath, List<string> errors)
	{
		string schemaPath = "schemas/" + schemaName;
		if (!entries.TryGetValue(schemaPath, out ZipArchiveEntry? schemaEntry))
		{
			errors.Add($"Thiếu schema {schemaPath} để kiểm tra {xmlPath}.");
			return;
		}

		XmlSchemaSet schemas = new XmlSchemaSet { XmlResolver = null };
		using (Stream schemaStream = schemaEntry.Open())
		using (XmlReader schemaReader = XmlReader.Create(schemaStream, SecureReaderSettings()))
			schemas.Add(null, schemaReader);
		schemas.Compile();

		XmlReaderSettings settings = SecureReaderSettings();
		settings.ValidationType = ValidationType.Schema;
		settings.Schemas.Add(schemas);
		settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
		settings.ValidationEventHandler += (sender, e) =>
		{
			if (e.Severity == XmlSeverityType.Error)
				errors.Add($"Schema {schemaPath}, XML {xmlPath}: {e.Message}");
		};
		using XmlReader reader = XmlReader.Create(new StringReader(document.ToString(SaveOptions.DisableFormatting)), settings);
		while (reader.Read()) { }
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

		foreach (XElement date in xml.Root.Elements().Where(e => new[] { "startDate", "endDate", "issuedDate", "ngayCap" }.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase)))
		{
			string value = date.Value.Trim();
			if (value.Length > 0 && !new[] { "yyyy-MM-dd", "dd/MM/yyyy", "MM/yyyy", "yyyy-MM", "yyyy" }.Any(format => DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
				errors.Add($"{path}: giá trị ngày <{date.Name.LocalName}> '{value}' không theo định dạng được chấp nhận.");
		}
	}

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
