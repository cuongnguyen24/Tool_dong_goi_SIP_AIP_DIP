using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using AXE.PackagingTool.ViewModels;
using ExcelDataReader;

namespace AXE.PackagingTool.Core;

public class MetadataDictionary
{
	private readonly Dictionary<string, Dictionary<string, string>> _dictionary = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

	private readonly Action<string, LogLevel> _log;

	public MetadataDictionary(Action<string, LogLevel> log)
	{
		_log = log;
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
	}

	public void Load(string danhMucFilePath)
	{
		if (!File.Exists(danhMucFilePath))
		{
			_log("⚠\ufe0f Không tìm thấy file Danh mục tại: " + danhMucFilePath + ". Việc phân tích ánh xạ danh mục sẽ bị bỏ qua.", LogLevel.Warning);
			return;
		}
		try
		{
			using FileStream fileStream = File.Open(danhMucFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using IExcelDataReader self = ExcelReaderFactory.CreateReader(fileStream);
			foreach (DataTable table in self.AsDataSet(new ExcelDataSetConfiguration
			{
				ConfigureDataTable = (IExcelDataReader _) => new ExcelDataTableConfiguration
				{
					UseHeaderRow = true
				}
			}).Tables)
			{
				string tableName = table.TableName;
				Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				int colIndex = GetColIndex(table, "Mã");
				int colIndex2 = GetColIndex(table, "Tên");
				if (colIndex < 0 || colIndex2 < 0)
				{
					continue;
				}
				foreach (DataRow row in table.Rows)
				{
					string text = row[colIndex]?.ToString()?.Trim();
					string value = row[colIndex2]?.ToString()?.Trim();
					if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(value))
					{
						dictionary[text] = value;
					}
				}
				_dictionary[tableName] = dictionary;
			}
			_log($"✅ Đã tải file Danh mục thành công ({_dictionary.Count} danh mục).", LogLevel.Success);
		}
		catch (Exception ex)
		{
			_log("❌ Lỗi đọc file Danh mục: " + ex.Message, LogLevel.Error);
		}
	}

	private int GetColIndex(DataTable table, string columnName)
	{
		for (int i = 0; i < table.Columns.Count; i++)
		{
			if (table.Columns[i].ColumnName.Contains(columnName, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	public string GetMappedValue(string categoryName, string code, string defaultValue = "")
	{
		if (string.IsNullOrEmpty(code))
		{
			return defaultValue;
		}
		if (_dictionary.TryGetValue(categoryName, out Dictionary<string, string> value) && value.TryGetValue(code, out var value2))
		{
			return value2;
		}
		if (!string.IsNullOrEmpty(defaultValue))
		{
			return defaultValue;
		}
		return code;
	}
}
