using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AXE.PackagingTool.Models;
using AXE.PackagingTool.ViewModels;

namespace AXE.PackagingTool.Core;

public class DynamicValidator
{
	private readonly string _baseFolder;

	private readonly Action<string, LogLevel, string, string, string> _log;

	public DynamicValidator(string baseFolder, Action<string, LogLevel, string, string, string> log)
	{
		_baseFolder = baseFolder;
		_log = log;
	}

	public bool Validate(List<ProfileDTO> profiles)
	{
		bool flag = false;
		_log($"\ud83d\udd0d Bắt đầu kiểm tra {profiles.Count} hồ sơ...", LogLevel.Info, "", "", "");
		foreach (ProfileDTO profile in profiles)
		{
			if (string.IsNullOrEmpty(profile.ProfileCode))
			{
				_log("❌ Hồ sơ không có mã (ProfileCode bị rỗng).", LogLevel.Error, "", "", "");
				flag = true;
				continue;
			}
			if (profile.Documents.Count == 0)
			{
				_log("⚠\ufe0f Hồ sơ " + profile.ProfileCode + " không có tài liệu nào.", LogLevel.Warning, profile.ProfileCode, "", "");
			}
			foreach (DocumentDTO document in profile.Documents)
			{
				string fileName = GetFileName(document);
				if (string.IsNullOrEmpty(fileName))
				{
					_log($"❌ [Dòng {document.ExcelRowIndex}] Tài liệu không có đường dẫn file (Cột Path bị trống).", LogLevel.Error, profile.ProfileCode, document.DocumentCode, document.ExcelRowIndex.ToString());
					document.IsPdfValid = false;
					flag = true;
					continue;
				}
				fileName = fileName.Replace("\r", "").Replace("\n", "").Trim();
				fileName = fileName.TrimStart('\\', '/');
				string text = (document.SourcePdfPath = Path.Combine(_baseFolder, fileName));
				if (!File.Exists(text))
				{
					string text3 = Path.Combine(_baseFolder, profile.ProfileCode, fileName);
					if (File.Exists(text3))
					{
						document.SourcePdfPath = text3;
						document.IsPdfValid = true;
						_log("✅ Tìm thấy file ở đường dẫn thay thế: " + text3, LogLevel.Success, profile.ProfileCode, document.DocumentCode, document.ExcelRowIndex.ToString());
						continue;
					}
					_log($"❌ [Dòng {document.ExcelRowIndex}] Không tìm thấy file: [{text}]", LogLevel.Error, profile.ProfileCode, document.DocumentCode, document.ExcelRowIndex.ToString());
					document.IsPdfValid = false;
					flag = true;
				}
				else
				{
					document.IsPdfValid = true;
				}
			}
		}
		return !flag;
	}

	private string GetFileName(DocumentDTO doc)
	{
		string[] array = new string[6] { "PathFile", "FilePath", "Path", "TepTin", "FileAttachment", "FileName" };
		foreach (string key in array)
		{
			string text = doc.Metadata.Keys.FirstOrDefault((string k) => k.Contains(key, StringComparison.OrdinalIgnoreCase));
			if (text != null && !string.IsNullOrWhiteSpace(doc.Metadata[text]))
			{
				return doc.Metadata[text].Replace("/", "\\");
			}
		}
		return "";
	}
}
