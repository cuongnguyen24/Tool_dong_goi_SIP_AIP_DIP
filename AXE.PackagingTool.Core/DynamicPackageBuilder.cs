using System;
using System.Buffers;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AXE.PackagingTool.Models;
using AXE.PackagingTool.ViewModels;

namespace AXE.PackagingTool.Core;

public class DynamicPackageBuilder
{
	private readonly IProgress<(int Percent, string Message)> _progress;

	private readonly Action<string, LogLevel, string, string, string> _log;

	public DynamicPackageBuilder(IProgress<(int Percent, string Message)> progress, Action<string, LogLevel, string, string, string> log)
	{
		_progress = progress;
		_log = log;
	}

	public string Build(ProfileDTO profile, PackageType pkgType, string outputFolder)
	{
		try
		{
			if (profile.Documents.Count > 0 && profile.Documents.All((DocumentDTO d) => !d.IsPdfValid))
			{
				_log("⚠\ufe0f Bỏ qua hồ sơ do không có tài liệu hợp lệ (tất cả đều trống Path hoặc không tồn tại file).", LogLevel.Warning, profile.ProfileCode, "", "");
				return null;
			}
			string text = pkgType.ToString();
			string text2 = string.Join("_", profile.ProfileCode.Split(Path.GetInvalidFileNameChars()));
			string text3 = text + "_" + text2;
			string text4 = Path.Combine(outputFolder, text3 + ".zip");
			_log("\ud83d\udce6 Bắt đầu đóng gói: " + text3 + ".zip", LogLevel.Info, profile.ProfileCode, "", "");
			long num = 0L;
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			using (FileStream stream = new FileStream(text4, FileMode.Create))
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
				zipArchive.CreateEntry("schemas/EAD.xsd");
				zipArchive.CreateEntry("schemas/EAD_doc.xsd");
				zipArchive.CreateEntry("schemas/EAD_media.xsd");
				zipArchive.CreateEntry("schemas/EAD_pic.xsd");
				zipArchive.CreateEntry("schemas/mets1-12.xsd");
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
				AddEadMetadata(zipArchive, text3, text, profile);
				AddEadMetadataRep(zipArchive, text, profile);
				switch (text)
				{
				case "SIP":
					AddMetsMapSIP(zipArchive, text3, profile, dictionary);
					AddMetsMapRepSIP(zipArchive, text3, profile, dictionary);
					break;
				case "AIP":
					AddPremisMetadataAIP(zipArchive, text3, profile, dictionary, text);
					AddMetsMapAIP(zipArchive, text3, profile, dictionary);
					AddMetsMapRepAIP(zipArchive, text3, profile, dictionary);
					break;
				case "DIP":
					AddPremisMetadataAIP(zipArchive, text3, profile, dictionary, text);
					AddMetsMapDIP(zipArchive, text3, profile, dictionary);
					AddMetsMapRepDIP(zipArchive, text3, profile, dictionary);
					break;
				}
			}
			_log($"✅ Hoàn thành gói {text3} với {dictionary.Count} tài liệu hợp lệ.", LogLevel.Success, profile.ProfileCode, "", "");
			return text4;
		}
		catch (Exception ex)
		{
			_log("❌ Lỗi khi đóng gói: " + ex.Message, LogLevel.Error, profile.ProfileCode, "", "");
			throw;
		}
	}

	private void AddEadMetadata(ZipArchive archive, string pID, string type, ProfileDTO profile)
	{
		using Stream output = archive.CreateEntry("metadata/descriptive/EAD.xml").Open();
		using XmlWriter xmlWriter = XmlWriter.Create(output, new XmlWriterSettings
		{
			Indent = true,
			Encoding = Encoding.UTF8
		});
		xmlWriter.WriteStartDocument();
		xmlWriter.WriteStartElement("simpledc");
		string name = type + "_ProfileFields";
		string text = ConfigurationManager.AppSettings[name];
		if (!string.IsNullOrEmpty(text))
		{
			text = Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);
			foreach (string item in from m in text.Split(',')
				select m.Trim())
			{
				string[] array = item.Split(':');
				string text2 = array[0].Trim();
				string field = ((array.Length > 1) ? array[1].Trim() : text2);
				string value = GetMetaByConfigName(profile.Metadata, field);
				if (string.IsNullOrEmpty(value) && text2.Equals("title", StringComparison.OrdinalIgnoreCase))
				{
					value = "Hồ sơ " + profile.ProfileCode;
				}
				if (string.IsNullOrEmpty(value) && (text2.Equals("fileCode", StringComparison.OrdinalIgnoreCase) || text2.Equals("arcFileCode", StringComparison.OrdinalIgnoreCase) || text2.Equals("maXacThuc", StringComparison.OrdinalIgnoreCase)))
				{
					value = profile.ProfileCode;
				}
				if (string.IsNullOrEmpty(value) && (text2.Equals("totalDoc", StringComparison.OrdinalIgnoreCase) || text2.Equals("soLuong", StringComparison.OrdinalIgnoreCase)))
				{
					value = profile.Documents.Count((DocumentDTO d) => d.IsPdfValid).ToString();
				}
				if (string.IsNullOrEmpty(value) && text2.Equals("maintenance", StringComparison.OrdinalIgnoreCase))
				{
					value = "01";
				}
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
				xmlWriter.WriteElementString(text3, item2.Value);
			}
		}
		xmlWriter.WriteEndElement();
	}

	private void AddEadMetadataRep(ZipArchive archive, string type, ProfileDTO profile)
	{
		string name = type + "_DocumentFields";
		string text = ConfigurationManager.AppSettings[name];
		if (!string.IsNullOrEmpty(text))
		{
			text = Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);
		}
		List<string> list = ((!string.IsNullOrEmpty(text)) ? (from f in text.Split(',')
			select f.Trim()).ToList() : null);
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
			string text3 = text2 + safeFileName + ".xml";
			hashSet.Add(text3);
			using Stream output = archive.CreateEntry("representations/rep1/metadata/descriptive/" + text3).Open();
			using XmlWriter xmlWriter = XmlWriter.Create(output, new XmlWriterSettings
			{
				Indent = true,
				Encoding = Encoding.UTF8
			});
			xmlWriter.WriteStartDocument();
			xmlWriter.WriteStartElement("simpledc");
			if (list != null)
			{
				foreach (string item in list)
				{
					string[] array = item.Split(':');
					string text4 = array[0].Trim();
					string field = ((array.Length > 1) ? array[1].Trim() : text4);
					string value2 = GetMetaByConfigName(document.Metadata, field);
					if (string.IsNullOrEmpty(value2) && text4.Equals("docId", StringComparison.OrdinalIgnoreCase))
					{
						value2 = document.DocumentCode;
					}
					if (string.IsNullOrEmpty(value2) && text4.Equals("maintenance", StringComparison.OrdinalIgnoreCase))
					{
						value2 = "01";
					}
					xmlWriter.WriteElementString(text4, value2);
				}
			}
			else
			{
				foreach (KeyValuePair<string, string> item2 in document.Metadata)
				{
					xmlWriter.WriteElementString(item2.Key, item2.Value);
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

	private void AddMetsMapSIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes)
	{
		using Stream output = archive.CreateEntry("METS.xml").Open();
		using XmlWriter xmlWriter = XmlWriter.Create(output, new XmlWriterSettings
		{
			Indent = true
		});
		xmlWriter.WriteStartDocument();
		xmlWriter.WriteStartElement("mets", "http://www.loc.gov/METS/");
		xmlWriter.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
		xmlWriter.WriteAttributeString("xmlns", "xlink", null, "http://www.w3.org/1999/xlink");
		xmlWriter.WriteAttributeString("OBJID", "uuid-" + Guid.NewGuid().ToString().ToUpper());
		xmlWriter.WriteAttributeString("LABEL", profile.ProfileCode);
		xmlWriter.WriteAttributeString("TYPE", "Mixed");
		xmlWriter.WriteAttributeString("CONTENTINFORMATIONTYPE", "Mixed");
		xmlWriter.WriteAttributeString("PROFILE", "https://earkcsip.dilcis.eu/profile/E-ARK-CSIP.xml");
		xmlWriter.WriteStartElement("metsHdr");
		xmlWriter.WriteAttributeString("CREATEDATE", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.ffK"));
		xmlWriter.WriteAttributeString("LASTMODDATE", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.ffK"));
		xmlWriter.WriteAttributeString("RECORDSTATUS", "NEW");
		xmlWriter.WriteAttributeString("OAISPACKAGETYPE", "SIP");
		xmlWriter.WriteStartElement("agent");
		xmlWriter.WriteAttributeString("ROLE", "CREATOR");
		xmlWriter.WriteAttributeString("TYPE", "OTHER");
		xmlWriter.WriteAttributeString("OTHERTYPE", "SOFTWARE");
		xmlWriter.WriteElementString("name", "AXE Packaging Tool");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement("dmdSec");
		xmlWriter.WriteAttributeString("ID", "uuid-" + Guid.NewGuid().ToString().ToUpper());
		xmlWriter.WriteStartElement("mdRef");
		xmlWriter.WriteAttributeString("LOCTYPE", "URL");
		xmlWriter.WriteAttributeString("MDTYPE", "OTHER");
		xmlWriter.WriteAttributeString("OTHERMDTYPE", "EAD");
		xmlWriter.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "metadata/descriptive/EAD.xml");
		xmlWriter.WriteAttributeString("MIMETYPE", "text/xml");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement("fileSec");
		xmlWriter.WriteStartElement("fileGrp");
		xmlWriter.WriteAttributeString("ID", "uuid-" + Guid.NewGuid().ToString().ToUpper());
		xmlWriter.WriteAttributeString("USE", "Representations");
		xmlWriter.WriteStartElement("file");
		xmlWriter.WriteAttributeString("ID", "uuid-" + Guid.NewGuid().ToString().ToUpper());
		xmlWriter.WriteAttributeString("MIMETYPE", "text/xml");
		xmlWriter.WriteStartElement("flocat");
		xmlWriter.WriteAttributeString("LOCTYPE", "URL");
		xmlWriter.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "representations/rep1/METS.xml");
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement("structMap");
		xmlWriter.WriteAttributeString("ID", "uuid-" + Guid.NewGuid().ToString().ToUpper());
		xmlWriter.WriteAttributeString("TYPE", "PHYSICAL");
		xmlWriter.WriteAttributeString("LABEL", "CSIP");
		xmlWriter.WriteStartElement("div");
		xmlWriter.WriteAttributeString("ID", "uuid-" + Guid.NewGuid().ToString().ToUpper());
		xmlWriter.WriteAttributeString("LABEL", profile.ProfileCode);
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
	}

	private void AddMetsMapRepSIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes)
	{
		using Stream output = archive.CreateEntry("representations/rep1/METS.xml").Open();
		using XmlWriter xmlWriter = XmlWriter.Create(output, new XmlWriterSettings
		{
			Indent = true
		});
		xmlWriter.WriteStartDocument();
		xmlWriter.WriteStartElement("mets", "http://www.loc.gov/METS/");
		xmlWriter.WriteAttributeString("xmlns", "xlink", null, "http://www.w3.org/1999/xlink");
		xmlWriter.WriteAttributeString("OBJID", "uuid-" + Guid.NewGuid().ToString().ToUpper());
		xmlWriter.WriteAttributeString("LABEL", "Representation 1");
		xmlWriter.WriteStartElement("fileSec");
		xmlWriter.WriteStartElement("fileGrp");
		xmlWriter.WriteAttributeString("USE", "Data");
		int num = 0;
		foreach (DocumentDTO item in profile.Documents.Where((DocumentDTO d) => d.IsPdfValid))
		{
			num++;
			xmlWriter.WriteStartElement("file");
			xmlWriter.WriteAttributeString("ID", "FILE" + num);
			if (hashes.TryGetValue(item.DocumentCode, out string value))
			{
				xmlWriter.WriteAttributeString("CHECKSUM", value);
				xmlWriter.WriteAttributeString("CHECKSUMTYPE", "SHA-256");
			}
			xmlWriter.WriteStartElement("flocat");
			xmlWriter.WriteAttributeString("LOCTYPE", "URL");
			xmlWriter.WriteAttributeString("xlink", "href", "http://www.w3.org/1999/xlink", "data/" + item.SafeFileName);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteStartElement("structMap");
		xmlWriter.WriteAttributeString("TYPE", "physical");
		xmlWriter.WriteStartElement("div");
		xmlWriter.WriteAttributeString("LABEL", "rep1");
		for (int num2 = 1; num2 <= num; num2++)
		{
			xmlWriter.WriteStartElement("div");
			xmlWriter.WriteStartElement("fptr");
			xmlWriter.WriteAttributeString("FILEID", "FILE" + num2);
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
		xmlWriter.WriteEndElement();
	}

	private void AddPremisMetadataAIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes, string typeTitle)
	{
		string[] array = new string[2] { "metadata/preservation/PREMIS.xml", "representations/rep1/metadata/preservation/PREMIS_rep1.xml" };
		foreach (string entryName in array)
		{
			using Stream output = archive.CreateEntry(entryName).Open();
			using XmlWriter xmlWriter = XmlWriter.Create(output, new XmlWriterSettings
			{
				Indent = true
			});
			xmlWriter.WriteStartDocument();
			xmlWriter.WriteStartElement("premis", "http://www.loc.gov/premis/v3");
			xmlWriter.WriteAttributeString("version", "3.0");
			foreach (DocumentDTO item in profile.Documents.Where((DocumentDTO d) => d.IsPdfValid))
			{
				if (hashes.ContainsKey(item.DocumentCode))
				{
					xmlWriter.WriteStartElement("object");
					xmlWriter.WriteAttributeString("type", "file");
					xmlWriter.WriteStartElement("objectIdentifier");
					xmlWriter.WriteElementString("objectIdentifierType", "local");
					xmlWriter.WriteElementString("objectIdentifierValue", item.DocumentCode);
					xmlWriter.WriteEndElement();
					xmlWriter.WriteStartElement("objectCharacteristics");
					FileInfo fileInfo = new FileInfo(item.SourcePdfPath);
					xmlWriter.WriteElementString("size", fileInfo.Length.ToString());
					xmlWriter.WriteStartElement("fixity");
					xmlWriter.WriteElementString("messageDigestAlgorithm", "SHA256");
					xmlWriter.WriteElementString("messageDigest", hashes[item.DocumentCode]);
					xmlWriter.WriteEndElement();
					xmlWriter.WriteEndElement();
					xmlWriter.WriteEndElement();
				}
			}
			xmlWriter.WriteStartElement("event");
			xmlWriter.WriteStartElement("eventIdentifier");
			xmlWriter.WriteElementString("eventIdentifierType", "local");
			xmlWriter.WriteElementString("eventIdentifierValue", Guid.NewGuid().ToString());
			xmlWriter.WriteEndElement();
			xmlWriter.WriteElementString("eventType", $"Đóng gói {typeTitle} ({typeTitle} Packaging)");
			xmlWriter.WriteElementString("eventDateTime", DateTime.Now.ToString("s"));
			xmlWriter.WriteElementString("eventDetail", "Đóng gói " + typeTitle + " theo Thông tư 05/2025/TT-BNV");
			xmlWriter.WriteEndElement();
			xmlWriter.WriteEndElement();
		}
	}

	private void AddMetsMapAIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes)
	{
		AddMetsMapSIP(archive, pID, profile, hashes);
	}

	private void AddMetsMapRepAIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes)
	{
		AddMetsMapRepSIP(archive, pID, profile, hashes);
	}

	private void AddMetsMapDIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes)
	{
		AddMetsMapSIP(archive, pID, profile, hashes);
	}

	private void AddMetsMapRepDIP(ZipArchive archive, string pID, ProfileDTO profile, Dictionary<string, string> hashes)
	{
		AddMetsMapRepSIP(archive, pID, profile, hashes);
	}
}
