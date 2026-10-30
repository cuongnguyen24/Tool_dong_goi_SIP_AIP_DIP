using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using AXE.PackagingTool.Models;
using AXE.PackagingTool.ViewModels;

namespace AXE.PackagingTool.Core;

public class DynamicPackageBuilder
{
	private static readonly ConcurrentDictionary<string, (string XmlField, string SourceField)[]> FieldConfigCache = new ConcurrentDictionary<string, (string, string)[]>(StringComparer.Ordinal);

	private readonly IProgress<(int Percent, string Message)> _progress;

	private readonly Action<string, LogLevel, string, string, string> _log;

	public DynamicPackageBuilder(IProgress<(int Percent, string Message)> progress, Action<string, LogLevel, string, string, string> log)
	{
		_progress = progress;
		_log = log;
	}

	public string Build(ProfileDTO profile, PackageType pkgType, string outputFolder, int submissionOrdinal = 1)
	{
		string tempPath = "";
		try
		{
			if (profile.Documents.Count > 0 && profile.Documents.All((DocumentDTO d) => !d.IsPdfValid))
			{
				_log("⚠\ufe0f Bỏ qua hồ sơ do không có tài liệu hợp lệ (tất cả đều trống Path hoặc không tồn tại file).", LogLevel.Warning, profile.ProfileCode, "", "");
				return null;
			}
			string text = pkgType.ToString();
			if (pkgType == PackageType.SIP && submissionOrdinal < 1)
			{
				throw new ArgumentOutOfRangeException(nameof(submissionOrdinal), "Số thứ tự hồ sơ SIP trong lần nộp phải lớn hơn 0.");
			}
			string text2 = string.Join("_", profile.ProfileCode.Split(Path.GetInvalidFileNameChars()));
			string sipPackageId = pkgType == PackageType.SIP ? "uuid-" + Guid.NewGuid().ToString().ToUpperInvariant() : "";
			string text3 = pkgType == PackageType.SIP ? submissionOrdinal + "_" + sipPackageId : text + "_" + text2;
			string text4 = Path.Combine(outputFolder, text3 + ".zip");
			tempPath = text4 + ".tmp";
			_log("\ud83d\udce6 Bắt đầu đóng gói: " + text3 + ".zip", LogLevel.Info, profile.ProfileCode, "", "");
			long num = 0L;
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			Dictionary<string, (long Size, string Checksum)> metadataFileInfo = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
			using (FileStream stream = new FileStream(tempPath, FileMode.Create))
			{
				using ZipArchive zipArchive = new ZipArchive(stream, ZipArchiveMode.Create);
				string text5 = "representations/rep1/data/";
				zipArchive.CreateEntry("metadata/descriptive/");
				if (text == "AIP" || text == "DIP")
				{
					zipArchive.CreateEntry("metadata/preservation/");
				}
				zipArchive.CreateEntry("representations/rep1/metadata/descriptive/");
				if (text == "AIP" || text == "DIP")
				{
					zipArchive.CreateEntry("representations/rep1/metadata/preservation/");
				}
				zipArchive.CreateEntry("schemas/");
				AddPackageSchemas(zipArchive, profile, metadataFileInfo);
				int num2 = 0;
				HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				Dictionary<string, int> dictionary3 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				foreach (DocumentDTO document in profile.Documents)
				{
					if (!document.IsPdfValid || string.IsNullOrEmpty(document.SourcePdfPath))
					{
						continue;
					}
					string fileName = Path.GetFileName(document.SourcePdfPath);
					if (!dictionary3.ContainsKey(fileName))
					{
						dictionary3[fileName] = 0;
						document.SafeFileName = fileName;
					}
					else
					{
						dictionary3[fileName]++;
						string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
						string extension = Path.GetExtension(fileName);
						document.SafeFileName = $"{fileNameWithoutExtension}_{dictionary3[fileName]}{extension}";
					}
					string entryName = text5 + document.SafeFileName;
					hashSet.Add(document.SafeFileName);
					using (Stream stream2 = zipArchive.CreateEntry(entryName, CompressionLevel.NoCompression).Open())
					{
						using FileStream fileStream = new FileStream(document.SourcePdfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
						using SHA256 sHA = SHA256.Create();
						byte[] array = ArrayPool<byte>.Shared.Rent(1048576);
						try
						{
							int num3;
							while ((num3 = fileStream.Read(array, 0, 1048576)) > 0)
							{
								stream2.Write(array, 0, num3);
								sHA.TransformBlock(array, 0, num3, null, 0);
							}
							sHA.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
							string value = BitConverter.ToString(sHA.Hash).Replace("-", "").ToLowerInvariant();
							dictionary[document.DocumentCode] = value;
							dictionary2[document.SourcePdfPath] = value;
						}
						finally
						{
							ArrayPool<byte>.Shared.Return(array);
						}
					}
					FileInfo fileInfo = new FileInfo(document.SourcePdfPath);
					num += fileInfo.Length;
					num2++;
				}
				AddEadMetadata(zipArchive, text3, text, profile, metadataFileInfo);
				AddEadMetadataRep(zipArchive, text, profile, metadataFileInfo);
				switch (text)
				{
				case "SIP":
					AddMetsMapSIP(zipArchive, text3, profile, dictionary, metadataFileInfo, sipPackageId);
					AddMetsMapRepSIP(zipArchive, text3, profile, dictionary, metadataFileInfo);
					break;
				case "AIP":
					AddPremisMetadataAIP(zipArchive, text3, profile, dictionary, text, metadataFileInfo);
					AddMetsMapAIP(zipArchive, text3, profile, dictionary, metadataFileInfo);
					AddMetsMapRepAIP(zipArchive, text3, profile, dictionary, metadataFileInfo);
					break;
				case "DIP":
					AddPremisMetadataAIP(zipArchive, text3, profile, dictionary, text, metadataFileInfo);
					AddMetsMapDIP(zipArchive, text3, profile, dictionary, metadataFileInfo);
					AddMetsMapRepDIP(zipArchive, text3, profile, dictionary, metadataFileInfo);
					break;
				}
			}
			using (FileStream validationStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (ZipArchive validationArchive = new ZipArchive(validationStream, ZipArchiveMode.Read))
			{
				PackageContentValidator.Validate(validationArchive, profile, pkgType, message => _log(message, LogLevel.Success, profile.ProfileCode, "", ""));
			}
			File.Move(tempPath, text4, overwrite: true);
			_log($"✅ Hoàn thành gói {text3} với {dictionary.Count} tài liệu hợp lệ.", LogLevel.Success, profile.ProfileCode, "", "");
			return text4;
		}
		catch (Exception ex)
		{
			if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
				File.Delete(tempPath);
			_log("❌ Lỗi khi đóng gói: " + ex, LogLevel.Error, profile.ProfileCode, "", "");
			throw;
		}
	}

	private void AddEadMetadata(ZipArchive archive, string pID, string type, ProfileDTO profile, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
	{
		using Stream output = CreateTrackedOutput(archive, "metadata/descriptive/EAD.xml", metadataFileInfo);
		using XmlWriter xmlWriter = XmlWriter.Create(output, new XmlWriterSettings
		{
			Indent = true,
			Encoding = Encoding.UTF8
		});
		xmlWriter.WriteStartDocument();
		xmlWriter.WriteStartElement("simpledc");
		xmlWriter.WriteAttributeString("xsi", "noNamespaceSchemaLocation", "http://www.w3.org/2001/XMLSchema-instance", "../../schemas/EAD.xsd");
		string name = type + "_ProfileFields";
		(string XmlField, string SourceField)[] fields = GetConfiguredFields(name);
		if (fields.Length > 0)
		{
			foreach ((string text2, string field) in fields)
			{
				string value = GetMetaByConfigName(profile.Metadata, field);
				if (string.IsNullOrEmpty(value) && text2.Equals("title", StringComparison.OrdinalIgnoreCase))
				{
					value = "Hồ sơ " + profile.ProfileCode;
				}
					if (string.IsNullOrEmpty(value) && (text2.Equals("fileCode", StringComparison.OrdinalIgnoreCase) || text2.Equals("arcFileCode", StringComparison.OrdinalIgnoreCase)))
				{
					value = profile.ProfileCode;
				}
					if (string.IsNullOrEmpty(value) && text2.Equals("totalDoc", StringComparison.OrdinalIgnoreCase))
				{
					value = profile.Documents.Count((DocumentDTO d) => d.IsPdfValid).ToString();
				}
				if (string.IsNullOrEmpty(value) && text2.Equals("maintenance", StringComparison.OrdinalIgnoreCase))
				{
					value = "01";
				}
				value = Tt05MetadataConverter.Convert(text2, value);
				xmlWriter.WriteElementString(text2, value);
			}
		}
		else
		{
			foreach (KeyValuePair<string, string> item2 in profile.Metadata)
			{
				string text3 = item2.Key;
				if (text3.EndsWith("hs", StringComparison.OrdinalIgnoreCase))
				{
					text3 = text3.Substring(0, text3.Length - 2);
				}
				xmlWriter.WriteElementString(text3, Tt05MetadataConverter.Convert(text3, item2.Value));
			}
		}
		xmlWriter.WriteEndElement();
	}

	private static void AddPackageSchemas(ZipArchive archive, ProfileDTO profile, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
	{
		AddEmbeddedSchema(archive, "METS", "METS.xsd", metadataFileInfo);
		AddEmbeddedSchema(archive, "xlink", "xlink.xsd", metadataFileInfo);
		AddEmbeddedSchema(archive, "EAD", "EAD.xsd", metadataFileInfo);

		HashSet<string> documentSchemaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (DocumentDTO document in profile.Documents.Where((DocumentDTO d) => d.IsPdfValid))
		{
			string extension = Path.GetExtension(document.SourcePdfPath).ToLowerInvariant();
			if (new[] { ".mp3", ".mp4", ".avi", ".wav", ".wmv", ".mov" }.Contains(extension))
			{
				documentSchemaNames.Add("EAD_media");
			}
			else if (new[] { ".jpg", ".jpeg", ".png", ".tiff", ".gif", ".bmp" }.Contains(extension))
			{
				documentSchemaNames.Add("EAD_pic");
			}
			else
			{
				documentSchemaNames.Add("EAD_doc");
			}
		}

		foreach (string schemaName in documentSchemaNames)
		{
			AddEmbeddedSchema(archive, schemaName, schemaName + ".xsd", metadataFileInfo);
		}
	}

	private static void AddEmbeddedSchema(ZipArchive archive, string resourceName, string fileName, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
	{
		string manifestName = "AXE.PackagingTool.Schemas." + resourceName + ".xsd";
		using Stream? schema = Assembly.GetExecutingAssembly().GetManifestResourceStream(manifestName);
		if (schema == null || schema.Length == 0)
		{
			throw new InvalidDataException("Không tìm thấy schema hoặc schema rỗng: " + manifestName);
		}

		byte[] bytes;
		using (MemoryStream buffer = new MemoryStream())
		{
			schema.CopyTo(buffer);
			bytes = buffer.ToArray();
		}
		string entryName = "schemas/" + fileName;
		ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
		using Stream destination = entry.Open();
		destination.Write(bytes, 0, bytes.Length);
		metadataFileInfo[entryName] = (bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)));
	}

	private void AddEadMetadataRep(ZipArchive archive, string type, ProfileDTO profile, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (DocumentDTO document in profile.Documents)
		{
			if (!document.IsPdfValid || string.IsNullOrEmpty(document.SourcePdfPath))
			{
				continue;
			}
			string safeFileName = document.SafeFileName;
			string value = Path.GetExtension(safeFileName).ToLower();
			string text2 = "EAD_doc_";
			if (new[] { ".mp3", ".mp4", ".avi", ".wav", ".wmv", ".mov" }.Contains(value))
			{
				text2 = "EAD_media_";
			}
			else
			{
				if (new[] { ".jpg", ".jpeg", ".png", ".tiff", ".gif", ".bmp" }.Contains(value))
				{
					text2 = "EAD_pic_";
				}
			}
			string fieldsSuffix = text2 == "EAD_media_" ? "_MediaFields" : text2 == "EAD_pic_" ? "_PictureFields" : "_DocumentFields";
			(string XmlField, string SourceField)[] fields = GetConfiguredFields(type + fieldsSuffix);
			string text3 = text2 + safeFileName + ".xml";
			hashSet.Add(text3);
			using Stream output = CreateTrackedOutput(archive, "representations/rep1/metadata/descriptive/" + text3, metadataFileInfo);
			using XmlWriter xmlWriter = XmlWriter.Create(output, new XmlWriterSettings
			{
				Indent = true,
				Encoding = Encoding.UTF8
			});
			xmlWriter.WriteStartDocument();
			xmlWriter.WriteStartElement("simpledc");
			string docSchema = text2.StartsWith("EAD_media_", StringComparison.Ordinal) ? "EAD_media.xsd" : text2.StartsWith("EAD_pic_", StringComparison.Ordinal) ? "EAD_pic.xsd" : "EAD_doc.xsd";
			xmlWriter.WriteAttributeString("xsi", "noNamespaceSchemaLocation", "http://www.w3.org/2001/XMLSchema-instance", "../../../../schemas/" + docSchema);
			if (fields.Length > 0)
			{
				foreach ((string text4, string field) in fields)
				{
					string value2 = GetMetaByConfigName(document.Metadata, field);
					if (string.IsNullOrEmpty(value2) && text4.Equals("docId", StringComparison.OrdinalIgnoreCase))
					{
						value2 = document.DocumentCode;
					}
					if (string.IsNullOrEmpty(value2) && (text4.Equals("docCode", StringComparison.OrdinalIgnoreCase) || text4.Equals("arcDocCode", StringComparison.OrdinalIgnoreCase)))
					{
						value2 = document.DocumentCode;
					}
					if (string.IsNullOrEmpty(value2) && text4.Equals("maintenance", StringComparison.OrdinalIgnoreCase))
					{
						value2 = "01";
					}
					value2 = Tt05MetadataConverter.Convert(text4, value2);
					xmlWriter.WriteElementString(text4, value2);
				}
			}
			else
			{
				foreach (KeyValuePair<string, string> item2 in document.Metadata)
				{
					xmlWriter.WriteElementString(item2.Key, Tt05MetadataConverter.Convert(item2.Key, item2.Value));
				}
			}
			xmlWriter.WriteEndElement();
		}
	}

	private string GetMetaByConfigName(Dictionary<string, string> meta, string field)
	{
		if (meta.ContainsKey(field))
		{
			return meta[field];
		}
		if (meta.ContainsKey(field + "hs"))
		{
			return meta[field + "hs"];
		}
		string text = meta.Keys.FirstOrDefault((string k) => string.Equals(k, field, StringComparison.OrdinalIgnoreCase) || string.Equals(k, field + "hs", StringComparison.OrdinalIgnoreCase));
		if (text != null)
		{
			return meta[text];
		}
		return "";
	}

	private static (string XmlField, string SourceField)[] GetConfiguredFields(string settingName)
	{
		string rawConfig = ConfigurationManager.AppSettings[settingName] ?? "";
		if (string.IsNullOrWhiteSpace(rawConfig))
			return Array.Empty<(string, string)>();

		string cacheKey = settingName + "\0" + rawConfig;
		return FieldConfigCache.GetOrAdd(cacheKey, static key =>
		{
			int separator = key.IndexOf('\0');
			string value = key.Substring(separator + 1);
			List<(string XmlField, string SourceField)> fields = new List<(string, string)>();
			foreach (string item in value.Split(','))
			{
				string fieldConfig = item.Trim();
				if (fieldConfig.Length == 0)
					continue;
				int colon = fieldConfig.IndexOf(':');
				string xmlField = colon >= 0 ? fieldConfig.Substring(0, colon).Trim() : fieldConfig;
				string sourceField = colon >= 0 ? fieldConfig.Substring(colon + 1).Trim() : xmlField;
				if (xmlField.Length > 0)
					fields.Add((xmlField, sourceField));
			}
			return fields.ToArray();
		});
	}

	private void AddMetsMapSIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes, Dictionary<string, (long Size, string Checksum)> metadataFileInfo, string packageId)
	{
		using Stream output = archive.CreateEntry("METS.xml").Open();
		using XmlWriter w = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 });
		const string mets = "http://www.loc.gov/METS/";
		const string xlink = "http://www.w3.org/1999/xlink";
		const string xsi = "http://www.w3.org/2001/XMLSchema-instance";
		const string csip = "https://DILCIS.eu/XML/METS/CSIPExtensionMETS";
		string repId = "uuid-" + Guid.NewGuid().ToString().ToUpperInvariant();
		string metadataDmdId = NewMetsId();
		string metadataFileId = NewMetsId();
		string schemaGroupId = NewMetsId();
		string representationGroupId = NewMetsId();
		string structMapId = NewMetsId();
		string rootDivId = NewMetsId();
		string metadataDivId = NewMetsId();
		string schemasDivId = NewMetsId();
		string repDivId = NewMetsId();
		DateTimeOffset now = DateTimeOffset.Now;
		w.WriteStartDocument();
		w.WriteStartElement("mets", mets);
		w.WriteAttributeString("xmlns", "xsi", null, xsi);
		w.WriteAttributeString("xmlns", "xlink", null, xlink);
		w.WriteAttributeString("xmlns", "csip", null, csip);
		w.WriteAttributeString("xsi", "schemaLocation", xsi, mets + " schemas/METS.xsd " + xlink + " schemas/xlink.xsd");
		w.WriteAttributeString("OBJID", packageId);
		w.WriteAttributeString("LABEL", profile.ProfileCode);
		w.WriteAttributeString("TYPE", "Mixed");
		w.WriteAttributeString("csip", "CONTENTINFORMATIONTYPE", csip, "MIXED");
		w.WriteAttributeString("PROFILE", "https://earkcsip.dilcis.eu/profile/E-ARK-CSIP.xml");
		w.WriteStartElement("metsHdr", mets);
		w.WriteAttributeString("CREATEDATE", now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
		w.WriteAttributeString("LASTMODDATE", now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
		w.WriteAttributeString("RECORDSTATUS", "NEW");
		w.WriteAttributeString("csip", "OAISPACKAGETYPE", csip, "SIP");
		w.WriteStartElement("agent", mets);
		w.WriteAttributeString("ROLE", "CREATOR");
		w.WriteAttributeString("TYPE", "OTHER");
		w.WriteAttributeString("OTHERTYPE", "SOFTWARE");
		w.WriteElementString("name", mets, "AXE Packaging Tool");
		w.WriteStartElement("note", mets);
		w.WriteAttributeString("csip", "NOTETYPE", csip, "SOFTWARE VERSION");
		w.WriteString(Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown");
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteStartElement("dmdSec", mets);
		w.WriteAttributeString("ID", metadataDmdId);
		w.WriteAttributeString("CREATED", now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
		var eadInfo = metadataFileInfo["metadata/descriptive/EAD.xml"];
		WriteSipMetadataRef(w, "metadata/descriptive/EAD.xml", "EAD", metadataFileId, now, eadInfo.Size, eadInfo.Checksum);
		w.WriteEndElement();
		w.WriteStartElement("fileSec", mets);
		w.WriteAttributeString("ID", NewMetsId());
		w.WriteStartElement("fileGrp", mets);
		w.WriteAttributeString("ID", schemaGroupId);
		w.WriteAttributeString("USE", "Schemas");
		foreach (string schemaPath in metadataFileInfo.Keys.Where(path => path.StartsWith("schemas/", StringComparison.OrdinalIgnoreCase)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
		{
			var info = metadataFileInfo[schemaPath];
			string fileId = NewMetsId();
			w.WriteStartElement("file", mets);
			w.WriteAttributeString("ID", fileId);
			w.WriteAttributeString("MIMETYPE", "application/xml");
			w.WriteAttributeString("SIZE", info.Size.ToString());
			w.WriteAttributeString("CHECKSUM", info.Checksum);
			w.WriteAttributeString("CHECKSUMTYPE", "SHA-256");
			w.WriteStartElement("FLocat", mets);
			w.WriteAttributeString("LOCTYPE", "URL");
			w.WriteAttributeString("xlink", "type", xlink, "simple");
			w.WriteAttributeString("xlink", "href", xlink, schemaPath);
			w.WriteEndElement();
			w.WriteEndElement();
		}
		w.WriteEndElement();
		w.WriteStartElement("fileGrp", mets);
		w.WriteAttributeString("ID", representationGroupId);
		w.WriteAttributeString("USE", "Representations/rep1");
		w.WriteStartElement("file", mets);
		w.WriteAttributeString("ID", repId);
		w.WriteAttributeString("MIMETYPE", "text/xml");
		w.WriteStartElement("FLocat", mets);
		w.WriteAttributeString("LOCTYPE", "URL");
		w.WriteAttributeString("xlink", "type", xlink, "simple");
		w.WriteAttributeString("xlink", "href", xlink, "representations/rep1/METS.xml");
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteStartElement("structMap", mets);
		w.WriteAttributeString("ID", structMapId);
		w.WriteAttributeString("TYPE", "PHYSICAL");
		w.WriteAttributeString("LABEL", "CSIP");
		w.WriteStartElement("div", mets);
		w.WriteAttributeString("ID", rootDivId);
		w.WriteAttributeString("LABEL", packageId);
		w.WriteStartElement("div", mets);
		w.WriteAttributeString("ID", metadataDivId);
		w.WriteAttributeString("LABEL", "Metadata");
		w.WriteAttributeString("DMDID", metadataDmdId);
		w.WriteEndElement();
		w.WriteStartElement("div", mets);
		w.WriteAttributeString("ID", schemasDivId);
		w.WriteAttributeString("LABEL", "Schemas");
		w.WriteStartElement("fptr", mets);
		w.WriteAttributeString("FILEID", schemaGroupId);
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteStartElement("div", mets);
		w.WriteAttributeString("ID", repDivId);
		w.WriteAttributeString("LABEL", "Representations/rep1");
		w.WriteStartElement("mptr", mets);
		w.WriteAttributeString("LOCTYPE", "URL");
		w.WriteAttributeString("xlink", "type", xlink, "simple");
		w.WriteAttributeString("xlink", "href", xlink, "representations/rep1/METS.xml");
		w.WriteAttributeString("xlink", "title", xlink, repId);
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteEndElement();
	}

	private void AddMetsMapRepSIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
	{
		using Stream output = archive.CreateEntry("representations/rep1/METS.xml").Open();
		using XmlWriter w = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 });
		const string mets = "http://www.loc.gov/METS/";
		const string xlink = "http://www.w3.org/1999/xlink";
		const string xsi = "http://www.w3.org/2001/XMLSchema-instance";
		const string csip = "https://DILCIS.eu/XML/METS/CSIPExtensionMETS";
		string objId = "uuid-" + Guid.NewGuid().ToString().ToUpperInvariant();
		DateTimeOffset now = DateTimeOffset.Now;
		w.WriteStartDocument();
		w.WriteStartElement("mets", mets);
		w.WriteAttributeString("xmlns", "xsi", null, xsi);
		w.WriteAttributeString("xmlns", "xlink", null, xlink);
		w.WriteAttributeString("xmlns", "csip", null, csip);
		w.WriteAttributeString("xsi", "schemaLocation", xsi, mets + " ../../schemas/METS.xsd " + xlink + " ../../schemas/xlink.xsd");
		w.WriteAttributeString("OBJID", objId);
		w.WriteAttributeString("LABEL", profile.ProfileCode);
		w.WriteAttributeString("TYPE", "Mixed");
		w.WriteAttributeString("csip", "CONTENTINFORMATIONTYPE", csip, "MIXED");
		w.WriteAttributeString("PROFILE", "https://earkcsip.dilcis.eu/profile/E-ARK-CSIP.xml");
		w.WriteStartElement("metsHdr", mets);
		w.WriteAttributeString("CREATEDATE", now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
		w.WriteAttributeString("RECORDSTATUS", "NEW");
		w.WriteStartElement("agent", mets);
		w.WriteAttributeString("ROLE", "CREATOR");
		w.WriteAttributeString("TYPE", "OTHER");
		w.WriteAttributeString("OTHERTYPE", "SOFTWARE");
		w.WriteElementString("name", mets, "AXE Packaging Tool");
		w.WriteEndElement();
		w.WriteEndElement();
		List<string> documentDmdIds = new List<string>();
		int documentIndex = 0;
		foreach (DocumentDTO document in profile.Documents.Where(d => d.IsPdfValid && !string.IsNullOrEmpty(d.SourcePdfPath)))
		{
			documentIndex++;
			string fileName = GetDocumentEadName(document);
			var info = metadataFileInfo["representations/rep1/metadata/descriptive/" + fileName];
			string documentDmdId = NewMetsId();
			documentDmdIds.Add(documentDmdId);
			w.WriteStartElement("dmdSec", mets);
			w.WriteAttributeString("ID", documentDmdId);
			w.WriteAttributeString("CREATED", now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
			WriteSipMetadataRef(w, "metadata/descriptive/" + fileName, "EAD", NewMetsId(), now, info.Size, info.Checksum);
			w.WriteEndElement();
		}
		w.WriteStartElement("fileSec", mets);
		w.WriteAttributeString("ID", NewMetsId());
		w.WriteStartElement("fileGrp", mets);
		w.WriteAttributeString("ID", NewMetsId());
		w.WriteAttributeString("USE", "Data");
		documentIndex = 0;
		foreach (DocumentDTO document in profile.Documents.Where(d => d.IsPdfValid && !string.IsNullOrEmpty(d.SourcePdfPath)))
		{
			documentIndex++;
			string fileId = "FILE" + documentIndex;
			FileInfo source = new FileInfo(document.SourcePdfPath);
			w.WriteStartElement("file", mets);
			w.WriteAttributeString("ID", fileId);
			w.WriteAttributeString("MIMETYPE", GetMimeType(document.SafeFileName));
			w.WriteAttributeString("SIZE", source.Length.ToString());
			w.WriteAttributeString("CREATED", now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
			if (hashes.TryGetValue(document.DocumentCode, out string? checksum))
			{
				w.WriteAttributeString("CHECKSUM", checksum.ToUpperInvariant());
				w.WriteAttributeString("CHECKSUMTYPE", "SHA-256");
			}
			w.WriteStartElement("FLocat", mets);
			w.WriteAttributeString("LOCTYPE", "URL");
			w.WriteAttributeString("xlink", "type", xlink, "simple");
			w.WriteAttributeString("xlink", "href", xlink, "data/" + document.SafeFileName);
			w.WriteEndElement();
			w.WriteEndElement();
		}
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteStartElement("structMap", mets);
		w.WriteAttributeString("ID", NewMetsId());
		w.WriteAttributeString("TYPE", "PHYSICAL");
		w.WriteStartElement("div", mets);
		w.WriteAttributeString("ID", NewMetsId());
		w.WriteAttributeString("LABEL", profile.ProfileCode);
		documentIndex = 0;
		foreach (DocumentDTO document in profile.Documents.Where(d => d.IsPdfValid && !string.IsNullOrEmpty(d.SourcePdfPath)))
		{
			documentIndex++;
			w.WriteStartElement("div", mets);
			w.WriteAttributeString("ID", NewMetsId());
			w.WriteAttributeString("LABEL", document.SafeFileName);
			w.WriteAttributeString("DMDID", documentDmdIds[documentIndex - 1]);
			w.WriteStartElement("fptr", mets);
			w.WriteAttributeString("FILEID", "FILE" + documentIndex);
			w.WriteEndElement();
			w.WriteEndElement();
		}
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteEndElement();
	}

	private static void WriteSipMetadataRef(XmlWriter w, string href, string otherMdType, string id, DateTimeOffset created, long size, string checksum)
	{
		const string mets = "http://www.loc.gov/METS/";
		const string xlink = "http://www.w3.org/1999/xlink";
		w.WriteStartElement("mdRef", mets);
		w.WriteAttributeString("ID", id);
		w.WriteAttributeString("LOCTYPE", "URL");
		w.WriteAttributeString("MDTYPE", "OTHER");
		w.WriteAttributeString("OTHERMDTYPE", otherMdType);
		w.WriteAttributeString("xlink", "type", xlink, "simple");
		w.WriteAttributeString("xlink", "href", xlink, href);
		w.WriteAttributeString("MIMETYPE", "text/xml");
		w.WriteAttributeString("SIZE", size.ToString());
		w.WriteAttributeString("CREATED", created.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
		w.WriteAttributeString("CHECKSUMTYPE", "SHA-256");
		w.WriteAttributeString("CHECKSUM", checksum);
		w.WriteEndElement();
	}

	private void AddPremisMetadataAIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes, string typeTitle, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
	{
		string[] array = new string[2] { "metadata/preservation/PREMIS.xml", "representations/rep1/metadata/preservation/PREMIS_rep1.xml" };
		foreach (string entryName in array)
		{
			using Stream output = CreateTrackedOutput(archive, entryName, metadataFileInfo);
			using XmlWriter xmlWriter = XmlWriter.Create(output, new XmlWriterSettings
			{
				Indent = true
			});
			xmlWriter.WriteStartDocument();
			const string premisNs = "http://www.loc.gov/premis/v3";
			xmlWriter.WriteStartElement("premis", "premis", premisNs);
			xmlWriter.WriteAttributeString("version", "3.0");
			foreach (DocumentDTO item in profile.Documents.Where((DocumentDTO d) => d.IsPdfValid))
			{
				if (hashes.ContainsKey(item.DocumentCode))
				{
					xmlWriter.WriteStartElement("premis", "object", premisNs);
					xmlWriter.WriteAttributeString("type", "file");
					xmlWriter.WriteStartElement("premis", "objectIdentifier", premisNs);
					xmlWriter.WriteElementString("premis", "objectIdentifierType", premisNs, "local");
					xmlWriter.WriteElementString("premis", "objectIdentifierValue", premisNs, item.DocumentCode);
					xmlWriter.WriteEndElement();
					xmlWriter.WriteStartElement("premis", "objectCharacteristics", premisNs);
					FileInfo fileInfo = new FileInfo(item.SourcePdfPath);
					xmlWriter.WriteElementString("premis", "size", premisNs, fileInfo.Length.ToString());
					xmlWriter.WriteStartElement("premis", "fixity", premisNs);
					xmlWriter.WriteElementString("premis", "messageDigestAlgorithm", premisNs, "SHA-256");
					xmlWriter.WriteElementString("premis", "messageDigest", premisNs, hashes[item.DocumentCode]);
					xmlWriter.WriteEndElement();
					xmlWriter.WriteStartElement("premis", "format", premisNs);
					xmlWriter.WriteStartElement("premis", "formatDesignation", premisNs);
					xmlWriter.WriteElementString("premis", "formatName", premisNs, Path.GetExtension(item.SafeFileName).TrimStart('.').ToUpperInvariant());
					xmlWriter.WriteEndElement();
					xmlWriter.WriteEndElement();
					xmlWriter.WriteEndElement();
					xmlWriter.WriteEndElement();
				}
			}
			xmlWriter.WriteStartElement("premis", "event", premisNs);
			xmlWriter.WriteStartElement("premis", "eventIdentifier", premisNs);
			xmlWriter.WriteElementString("premis", "eventIdentifierType", premisNs, "local");
			xmlWriter.WriteElementString("premis", "eventIdentifierValue", premisNs, Guid.NewGuid().ToString().ToUpperInvariant());
			xmlWriter.WriteEndElement();
			xmlWriter.WriteElementString("premis", "eventType", premisNs, $"Đóng gói {typeTitle} ({typeTitle} Packaging)");
			xmlWriter.WriteElementString("premis", "eventDateTime", premisNs, DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
			xmlWriter.WriteStartElement("premis", "eventDetailInformation", premisNs);
			xmlWriter.WriteElementString("premis", "eventDetail", premisNs, "Đóng gói " + typeTitle + " theo Thông tư 05/2025/TT-BNV; tạo SHA-256 cho từng tệp dữ liệu.");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteStartElement("premis", "eventOutcomeInformation", premisNs);
			xmlWriter.WriteElementString("premis", "eventOutcome", premisNs, "success");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
	}

	private void AddMetsMapAIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
		=> AddMetsMapPackage(archive, pID, profile, hashes, metadataFileInfo, "AIP");

	private void AddMetsMapPackage(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes, Dictionary<string, (long Size, string Checksum)> metadataFileInfo, string packageType)
	{
		using Stream output = archive.CreateEntry("METS.xml").Open();
		using XmlWriter w = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 });
		string packageId = packageType == "AIP"
			? "urn:" + GetFondCode(profile) + ":uuid:" + Guid.NewGuid().ToString().ToUpperInvariant()
			: "uuid-" + Guid.NewGuid().ToString().ToUpperInvariant();
		WritePackageMetsRoot(w, profile, packageId, false, packageType);
		string dmdId = NewMetsId();
		string amdId = NewMetsId();
		w.WriteStartElement("dmdSec", "http://www.loc.gov/METS/");
		w.WriteAttributeString("ID", dmdId);
		w.WriteAttributeString("CREATED", DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
		WriteMetadataRef(w, "metadata/descriptive/EAD.xml", "EAD", "text/xml", metadataFileInfo["metadata/descriptive/EAD.xml"]);
		w.WriteEndElement();
		w.WriteStartElement("amdSec", "http://www.loc.gov/METS/");
		w.WriteAttributeString("ID", amdId);
		w.WriteStartElement("digiprovMD", "http://www.loc.gov/METS/");
		w.WriteAttributeString("ID", NewMetsId());
		WriteMetadataRef(w, "metadata/preservation/PREMIS.xml", "PREMIS", "text/xml", metadataFileInfo["metadata/preservation/PREMIS.xml"]);
		w.WriteEndElement();
		w.WriteEndElement();
		w.WriteStartElement("fileSec", "http://www.loc.gov/METS/");
		w.WriteAttributeString("ID", NewMetsId());
		w.WriteStartElement("fileGrp", "http://www.loc.gov/METS/");
		string representationGroupId = NewMetsId();
		w.WriteAttributeString("ID", representationGroupId);
		w.WriteAttributeString("USE", "Representations");
		w.WriteStartElement("file", "http://www.loc.gov/METS/");
		w.WriteAttributeString("ID", "REP1_METS");
		w.WriteAttributeString("MIMETYPE", "text/xml");
		w.WriteStartElement("FLocat", "http://www.loc.gov/METS/");
		w.WriteAttributeString("LOCTYPE", "URL");
		w.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
		w.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "representations/rep1/METS.xml");
		w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();
		w.WriteStartElement("structMap", "http://www.loc.gov/METS/");
		w.WriteAttributeString("ID", NewMetsId()); w.WriteAttributeString("TYPE", "PHYSICAL"); w.WriteAttributeString("LABEL", "CSIP");
		w.WriteStartElement("div", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", NewMetsId()); w.WriteAttributeString("LABEL", profile.ProfileCode);
		w.WriteStartElement("div", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", NewMetsId()); w.WriteAttributeString("LABEL", "Metadata");
		w.WriteAttributeString("DMDID", dmdId); w.WriteAttributeString("ADMID", amdId); w.WriteEndElement();
		w.WriteStartElement("div", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", NewMetsId()); w.WriteAttributeString("LABEL", "Representations/rep1");
		w.WriteStartElement("mptr", "http://www.loc.gov/METS/"); w.WriteAttributeString("LOCTYPE", "URL");
		w.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
		w.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "representations/rep1/METS.xml");
		w.WriteAttributeString("xlink", "title", "http://www.w3.org/1999/xlink", representationGroupId);
		w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();
		w.WriteEndElement();
	}

	private void AddMetsMapRepAIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
		=> AddMetsMapRepPackage(archive, pID, profile, hashes, metadataFileInfo, "AIP");

	private void AddMetsMapRepPackage(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes, Dictionary<string, (long Size, string Checksum)> metadataFileInfo, string packageType)
	{
		using Stream output = archive.CreateEntry("representations/rep1/METS.xml").Open();
		using XmlWriter w = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 });
		string representationId = packageType == "AIP"
			? "urn:" + GetFondCode(profile) + ":rep1:uuid:" + Guid.NewGuid().ToString().ToUpperInvariant()
			: "uuid-" + Guid.NewGuid().ToString().ToUpperInvariant();
		WritePackageMetsRoot(w, profile, representationId, true, packageType);
		string dmdId = NewMetsId();
		string amdId = NewMetsId();
		w.WriteStartElement("dmdSec", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", dmdId); w.WriteAttributeString("CREATED", DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
		int documentIndex = 0;
		foreach (DocumentDTO document in profile.Documents.Where(d => d.IsPdfValid && !string.IsNullOrEmpty(d.SourcePdfPath)))
		{
			documentIndex++;
			string eadName = GetDocumentEadName(document);
			string metadataPath = "representations/rep1/metadata/descriptive/" + eadName;
			WriteMetadataRef(w, "metadata/descriptive/" + eadName, "EAD", "text/xml", metadataFileInfo[metadataPath]);
		}
		w.WriteEndElement();
		w.WriteStartElement("amdSec", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", amdId);
		w.WriteStartElement("digiprovMD", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", NewMetsId());
		WriteMetadataRef(w, "metadata/preservation/PREMIS_rep1.xml", "PREMIS", "text/xml", metadataFileInfo["representations/rep1/metadata/preservation/PREMIS_rep1.xml"]);
		w.WriteEndElement(); w.WriteEndElement();
		w.WriteStartElement("fileSec", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", NewMetsId());
		w.WriteStartElement("fileGrp", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", NewMetsId()); w.WriteAttributeString("USE", "Data");
		documentIndex = 0;
		foreach (DocumentDTO document in profile.Documents.Where(d => d.IsPdfValid && !string.IsNullOrEmpty(d.SourcePdfPath)))
		{
			documentIndex++;
			string fileId = "FILE" + documentIndex;
			w.WriteStartElement("file", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", fileId);
			w.WriteAttributeString("MIMETYPE", GetMimeType(document.SafeFileName));
			w.WriteAttributeString("SIZE", new FileInfo(document.SourcePdfPath).Length.ToString());
			w.WriteAttributeString("CREATED", DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
			if (hashes.TryGetValue(document.DocumentCode, out string? digest)) { w.WriteAttributeString("CHECKSUM", digest.ToUpperInvariant()); w.WriteAttributeString("CHECKSUMTYPE", "SHA-256"); }
			w.WriteStartElement("FLocat", "http://www.loc.gov/METS/"); w.WriteAttributeString("LOCTYPE", "URL");
			w.WriteAttributeString("xlink", "type", "http://www.w3.org/1999/xlink", "simple");
			w.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "data/" + document.SafeFileName);
			w.WriteEndElement(); w.WriteEndElement();
		}
		w.WriteEndElement(); w.WriteEndElement();
		w.WriteStartElement("structMap", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", NewMetsId()); w.WriteAttributeString("TYPE", "PHYSICAL");
		w.WriteStartElement("div", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", NewMetsId()); w.WriteAttributeString("LABEL", "rep1"); w.WriteAttributeString("DMDID", dmdId); w.WriteAttributeString("ADMID", amdId);
		documentIndex = 0;
		foreach (DocumentDTO document in profile.Documents.Where(d => d.IsPdfValid && !string.IsNullOrEmpty(d.SourcePdfPath)))
		{
			documentIndex++;
			w.WriteStartElement("div", "http://www.loc.gov/METS/"); w.WriteAttributeString("ID", NewMetsId()); w.WriteAttributeString("LABEL", document.SafeFileName);
			w.WriteStartElement("fptr", "http://www.loc.gov/METS/"); w.WriteAttributeString("FILEID", "FILE" + documentIndex); w.WriteEndElement();
			w.WriteEndElement();
		}
		w.WriteEndElement(); w.WriteEndElement();
		w.WriteEndElement();
	}

	private static string NewMetsId() => "uuid-" + Guid.NewGuid().ToString().ToUpperInvariant();

	private static Stream CreateTrackedOutput(ZipArchive archive, string entryName, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
	{
		ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
		return new MetadataTrackingStream(entry.Open(), info => metadataFileInfo[entryName] = info);
	}

	private static string GetFondCode(ProfileDTO profile)
	{
		string? fondCode = profile.Metadata.FirstOrDefault(item => new[] { "fondCode", "fondcode", "maPhong", "MaPhong", "maPhonghs", "FondCodehs" }
			.Contains(item.Key, StringComparer.OrdinalIgnoreCase)).Value;
		return string.IsNullOrWhiteSpace(fondCode) ? profile.ProfileCode : fondCode.Trim();
	}

	private static string GetDocumentEadName(DocumentDTO document)
	{
		string ext = Path.GetExtension(document.SafeFileName).ToLowerInvariant();
		string prefix = new[] { ".mp3", ".mp4", ".avi", ".wav", ".wmv", ".mov" }.Contains(ext) ? "EAD_media_" :
			new[] { ".jpg", ".jpeg", ".png", ".tiff", ".gif", ".bmp" }.Contains(ext) ? "EAD_pic_" : "EAD_doc_";
		return prefix + document.SafeFileName + ".xml";
	}

	private static string GetMimeType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
	{
		".pdf" => "application/pdf", ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".tif" or ".tiff" => "image/tiff",
		".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".mp4" => "video/mp4", ".avi" => "video/x-msvideo", ".mov" => "video/quicktime",
		_ => "application/octet-stream"
	};

	private static void WritePackageMetsRoot(XmlWriter w, ProfileDTO profile, string objId, bool representation, string packageType)
	{
		const string mets = "http://www.loc.gov/METS/";
		const string xsi = "http://www.w3.org/2001/XMLSchema-instance";
		const string xlink = "http://www.w3.org/1999/xlink";
		const string csip = "https://DILCIS.eu/XML/METS/CSIPExtensionMETS";
		w.WriteStartDocument(); w.WriteStartElement("mets", mets);
		w.WriteAttributeString("xmlns", "xsi", null, xsi); w.WriteAttributeString("xmlns", "xlink", null, xlink); w.WriteAttributeString("xmlns", "csip", null, csip);
		string schemaFolder = representation ? "../../schemas/" : "schemas/";
		w.WriteAttributeString("xsi", "schemaLocation", xsi, mets + " " + schemaFolder + "METS.xsd " + xlink + " " + schemaFolder + "xlink.xsd");
		w.WriteAttributeString("OBJID", objId); w.WriteAttributeString("LABEL", profile.ProfileCode); w.WriteAttributeString("TYPE", "Mixed");
		w.WriteAttributeString("csip", "CONTENTINFORMATIONTYPE", csip, "MIXED");
		w.WriteAttributeString("PROFILE", packageType == "DIP" ? "https://earkdip.dilcis.eu/profile/E-ARK-DIP.xml" : "https://earkcsip.dilcis.eu/profile/E-ARK-CSIP.xml");
		w.WriteStartElement("metsHdr", mets); w.WriteAttributeString("CREATEDATE", DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
		w.WriteAttributeString("LASTMODDATE", DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz")); w.WriteAttributeString("RECORDSTATUS", "NEW");
		w.WriteAttributeString("csip", "OAISPACKAGETYPE", csip, packageType);
		w.WriteStartElement("agent", mets); w.WriteAttributeString("ROLE", "CREATOR"); w.WriteAttributeString("TYPE", "OTHER"); w.WriteAttributeString("OTHERTYPE", "SOFTWARE");
		w.WriteElementString("name", mets, "AXE Packaging Tool");
		w.WriteStartElement("note", mets); w.WriteAttributeString("csip", "NOTETYPE", csip, "SOFTWARE VERSION"); w.WriteString(Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"); w.WriteEndElement();
		w.WriteEndElement(); w.WriteEndElement();
	}

	private static void WriteMetadataRef(XmlWriter w, string href, string otherMdType, string mimeType, (long Size, string Checksum) fileInfo)
	{
		const string mets = "http://www.loc.gov/METS/"; const string xlink = "http://www.w3.org/1999/xlink";
		if (fileInfo.Size <= 0 || string.IsNullOrWhiteSpace(fileInfo.Checksum))
			throw new InvalidDataException("Không thể tạo mdRef vì tệp metadata bị thiếu hoặc rỗng: " + href);
		w.WriteStartElement("mdRef", mets); w.WriteAttributeString("ID", NewMetsId()); w.WriteAttributeString("LOCTYPE", "URL"); w.WriteAttributeString("MDTYPE", "OTHER");
		w.WriteAttributeString("OTHERMDTYPE", otherMdType); w.WriteAttributeString("MDTYPEVERSION", "1.0");
		w.WriteAttributeString("xlink", "type", xlink, "simple"); w.WriteAttributeString("xlink", "href", xlink, href); w.WriteAttributeString("MIMETYPE", mimeType);
		w.WriteAttributeString("SIZE", fileInfo.Size.ToString()); w.WriteAttributeString("CREATED", DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
		w.WriteAttributeString("CHECKSUMTYPE", "SHA-256");
		w.WriteAttributeString("CHECKSUM", fileInfo.Checksum);
		w.WriteEndElement();
	}

	private sealed class MetadataTrackingStream : Stream
	{
		private readonly Stream _destination;
		private readonly MemoryStream _buffer = new MemoryStream();
		private readonly Action<(long Size, string Checksum)> _onComplete;
		private bool _disposed;

		public MetadataTrackingStream(Stream destination, Action<(long Size, string Checksum)> onComplete)
		{
			_destination = destination;
			_onComplete = onComplete;
		}

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => !_disposed;
		public override long Length => _buffer.Length;
		public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }
		public override void Flush() => _destination.Flush();
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
		{
			_destination.Write(buffer, offset, count);
			_buffer.Write(buffer, offset, count);
		}

		public override void Write(ReadOnlySpan<byte> buffer)
		{
			_destination.Write(buffer);
			_buffer.Write(buffer);
		}

		protected override void Dispose(bool disposing)
		{
			if (!_disposed && disposing)
			{
				_destination.Dispose();
				byte[] bytes = _buffer.ToArray();
				using SHA256 sha = SHA256.Create();
				_onComplete((bytes.LongLength, Convert.ToHexString(sha.ComputeHash(bytes))));
				_buffer.Dispose();
				_disposed = true;
			}
			base.Dispose(disposing);
		}
	}

	private void AddMetsMapDIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
		=> AddMetsMapPackage(archive, pID, profile, hashes, metadataFileInfo, "DIP");

	private void AddMetsMapRepDIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes, Dictionary<string, (long Size, string Checksum)> metadataFileInfo)
		=> AddMetsMapRepPackage(archive, pID, profile, hashes, metadataFileInfo, "DIP");
}
