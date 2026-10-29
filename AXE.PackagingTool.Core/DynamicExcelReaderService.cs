using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AXE.PackagingTool.Models;
using AXE.PackagingTool.ViewModels;
using ExcelDataReader;

namespace AXE.PackagingTool.Core;

public class DynamicExcelReaderService
{
	private readonly Action<string, LogLevel, string, string, string> _log;

	private static readonly Dictionary<string, string> TagToSheetNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		{ "maintenance", "Thời hạn bảo quản" },
		{ "mode", "Chế độ sử dụng" },
		{ "language", "Ngôn ngữ" },
		{ "format", "Tình trạng vật lý" },
		{ "confidenceLevel", "Mức độ tin cậy" },
		{ "riskRecovery", "Khả năng phục hồi" },
		{ "riskRecoveryStatus", "Tình trạng phục hồi" },
		{ "typeName", "Loại văn bản" },
		{ "loaiTaiLieu", "Loại văn bản" },
		{ "process", "Quy trình xử lý" }
	};

	public DynamicExcelReaderService(Action<string, LogLevel, string, string, string> log)
	{
		_log = log;
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
	}

	public List<ProfileDTO> ReadExcel(string filePath, string groupingColumn)
	{
		_log("\ud83d\udcc2 Đọc file Excel: " + Path.GetFileName(filePath), LogLevel.Info, "", "", "");
		List<ProfileDTO> list = new List<ProfileDTO>();
		try
		{
			using (FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				using IExcelDataReader self = ExcelReaderFactory.CreateReader(fileStream);
				DataSet dataSet = self.AsDataSet();
				if (dataSet.Tables.Count == 0)
				{
					return list;
				}
				DataTable dataTable = dataSet.Tables[0];
				if (dataTable.Rows.Count < 3)
				{
					_log("❌ File Excel không đủ 3 dòng header tiêu chuẩn.", LogLevel.Error, "", "", "");
					return list;
				}
				Dictionary<string, Dictionary<string, string>> dictionary = LoadAllDictionaries(_log);
				Dictionary<int, string> dictionary2 = BuildMap(dataTable.Rows[1]);
				Dictionary<int, string> dictionary3 = BuildMap(dataTable.Rows[2]);
				if (!dictionary2.ContainsValue(groupingColumn))
				{
					_log("❌ Không tìm thấy cột gom nhóm '" + groupingColumn + "' ở dòng 2.", LogLevel.Error, "", "", "");
					return list;
				}
				_log("ℹ️ Bỏ qua dữ liệu phông; gom hồ sơ theo cột '" + groupingColumn + "'.", LogLevel.Info, "", "", "");
				int groupColIndex = dictionary2.FirstOrDefault((KeyValuePair<int, string> x) => x.Value == groupingColumn).Key;
				int num = 0;
				List<DataRow> list2 = new List<DataRow>();
				Dictionary<DataRow, int> rowIndexes = new Dictionary<DataRow, int>();
				for (int num2 = 3; num2 < dataTable.Rows.Count; num2++)
				{
					DataRow dataRow = dataTable.Rows[num2];
					bool flag = true;
					for (int num3 = 0; num3 < dataTable.Columns.Count; num3++)
					{
						if (!string.IsNullOrWhiteSpace(dataRow[num3]?.ToString()))
						{
							flag = false;
							break;
						}
					}
					if (flag)
					{
						num++;
						if (num >= 20)
						{
							break;
						}
					}
					else
					{
						num = 0;
						list2.Add(dataRow);
						rowIndexes[dataRow] = num2 + 1;
					}
				}
				foreach (IGrouping<string, DataRow> item in from r in list2
					group r by r[groupColIndex]?.ToString()?.Trim() ?? "")
				{
					if (string.IsNullOrEmpty(item.Key))
					{
						continue;
					}
					DataRow dataRow2 = item.First();
					ProfileDTO profileDTO = new ProfileDTO
					{
						ProfileCode = item.Key
					};
					foreach (KeyValuePair<int, string> item2 in dictionary2)
					{
						if (!string.IsNullOrEmpty(item2.Value) && item2.Value != "#VALUE!")
						{
							string value = dataRow2[item2.Key]?.ToString()?.Trim() ?? "";
							profileDTO.Metadata[item2.Value] = value;
						}
					}
					foreach (DataRow item3 in item)
					{
						DocumentDTO documentDTO = new DocumentDTO();
						documentDTO.ExcelRowIndex = rowIndexes[item3];
						foreach (KeyValuePair<int, string> item4 in dictionary3)
						{
							if (string.IsNullOrEmpty(item4.Value) || !(item4.Value != "#VALUE!"))
							{
								continue;
							}
							string text = item3[item4.Key]?.ToString()?.Trim() ?? "";
							if (string.IsNullOrEmpty(text) && item4.Value.Equals("maintenance", StringComparison.OrdinalIgnoreCase))
							{
								text = "01";
							}
							documentDTO.Metadata[item4.Value] = text;
							if ((item4.Value.Equals("DocID", StringComparison.OrdinalIgnoreCase) || item4.Value.Equals("DocCode", StringComparison.OrdinalIgnoreCase)) && string.IsNullOrEmpty(documentDTO.DocumentCode))
							{
								documentDTO.DocumentCode = text;
							}
							if (TagToSheetNameMap.TryGetValue(item4.Value, out string value2))
							{
								string key = RemoveSign(NormalizeForDict(value2));
								string key2 = RemoveSign(NormalizeForDict(text));
								if (dictionary.TryGetValue(key, out var value3) && value3.TryGetValue(key2, out var value4))
								{
									text = value4;
									documentDTO.Metadata[item4.Value] = text;
								}
							}
						}
						profileDTO.Documents.Add(documentDTO);
					}
					list.Add(profileDTO);
				}
			}
			_log($"✅ Đã đọc {list.Count} hồ sơ, {list.Sum((ProfileDTO p) => p.Documents.Count)} tài liệu.", LogLevel.Success, "", "", "");
		}
		catch (Exception ex)
		{
			_log("❌ Lỗi đọc Excel: " + ex.Message, LogLevel.Error, "", "", "");
		}
		return list;
	}

	private Dictionary<int, string> BuildMap(DataRow row)
	{
		Dictionary<int, string> dictionary = new Dictionary<int, string>();
		for (int i = 0; i < row.ItemArray.Length; i++)
		{
			string value = row[i]?.ToString()?.Trim() ?? "";
			dictionary[i] = value;
		}
		return dictionary;
	}

	public static Dictionary<string, Dictionary<string, string>> LoadAllDictionaries(Action<string, LogLevel, string, string, string>? log = null)
	{
		Dictionary<string, Dictionary<string, string>> dictionary = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
		try
		{
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Metadata", "DanhMuc.xlsx");
			if (!File.Exists(text))
			{
				log?.Invoke("❌ Không tìm thấy file danh mục tại '" + text + "'", LogLevel.Error, "", "", "");
				return dictionary;
			}
			using FileStream fileStream = File.Open(text, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using IExcelDataReader self = ExcelReaderFactory.CreateReader(fileStream);
			foreach (DataTable table in self.AsDataSet().Tables)
			{
				string key = RemoveSign(NormalizeForDict(table.TableName));
				Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				int num = -1;
				int num2 = -1;
				for (int i = 0; i < Math.Min(3, table.Rows.Count); i++)
				{
					for (int j = 0; j < table.Columns.Count; j++)
					{
						string text2 = table.Rows[i][j]?.ToString()?.Trim()?.ToLower() ?? "";
						if (text2 == "mã" || text2 == "ma" || text2.Contains("mã loại") || text2.Contains("mã tài liệu"))
						{
							num = j;
						}
						if (text2 == "tên" || text2 == "ten" || text2.Contains("tên loại") || text2.Contains("tên tài liệu"))
						{
							num2 = j;
						}
					}
					if (num >= 0 && num2 >= 0)
					{
						break;
					}
				}
				if (num == -1 || num2 == -1)
				{
					if (table.Columns.Count >= 3)
					{
						num = 1;
						num2 = 2;
					}
					else
					{
						num = 0;
						num2 = 1;
					}
				}
				for (int k = 0; k < table.Rows.Count; k++)
				{
					string text3 = table.Rows[k][num]?.ToString()?.Trim() ?? "";
					string text4 = table.Rows[k][num2]?.ToString()?.Trim() ?? "";
					if (!string.IsNullOrEmpty(text3) && !string.IsNullOrEmpty(text4) && text3.Length < text4.Length)
					{
						dictionary2[RemoveSign(NormalizeForDict(text4))] = text3;
					}
				}
				dictionary[key] = dictionary2;
			}
		}
		catch (Exception ex)
		{
			log?.Invoke("❌ Lỗi đọc file danh mục: " + ex.Message, LogLevel.Error, "", "", "");
		}
		return dictionary;
	}

	public static string NormalizeForDict(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return "";
		}
		return Regex.Replace(input.Replace('\u00a0', ' ').Replace("\u200b", "").Normalize(NormalizationForm.FormC)
			.ToLowerInvariant()
			.Trim(), "\\s+", " ");
	}

	public static string RemoveSign(string str)
	{
		if (string.IsNullOrEmpty(str))
		{
			return "";
		}
		string[] array = new string[14]
		{
			"a", "A", "e", "E", "o", "O", "u", "U", "i", "I",
			"d", "D", "y", "Y"
		};
		string[] array2 = new string[14]
		{
			"áàạảãâấầậẩẫăắằặẳẵ", "ÁÀẠẢÃÂẤẦẬẨẪĂẮẰẶẲẴ", "éèẹẻẽêếềệểễ", "ÉÈẸẺẼÊẾỀỆỂỄ", "óòọỏõôốồộổỗơớờợởỡ", "ÓÒỌỎÕÔỐỒỘỔỖƠỚỜỢỞỠ", "úùụủũưứừựửữ", "ÚÙỤỦŨƯỨỪỰỬỮ", "íìịỉĩ", "ÍÌỊỈĨ",
			"đ", "Đ", "ýỳỵỷỹ", "ÝỲỴỶỸ"
		};
		for (int i = 0; i < array2.Length; i++)
		{
			string text = array2[i];
			foreach (char oldChar in text)
			{
				str = str.Replace(oldChar, array[i][0]);
			}
		}
		return str;
	}
}
