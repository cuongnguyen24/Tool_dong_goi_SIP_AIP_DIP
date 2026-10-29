using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AXE.PackagingTool.Core;

/// <summary>Converts TT05 controlled-vocabulary labels to their published codes.</summary>
internal static class Tt05MetadataConverter
{
	private static readonly Dictionary<string, Dictionary<string, string>> CodeLists = new(StringComparer.OrdinalIgnoreCase)
	{
		["maintenance"] = Map(
			("01", "Vĩnh viễn"), ("02", "70 năm"), ("03", "50 năm"), ("04", "30 năm"),
			("05", "20 năm"), ("06", "10 năm"), ("07", "Khác")),
		["mode"] = Map(("01", "Công khai"), ("02", "Sử dụng có điều kiện"), ("03", "Mật")),
		["language"] = Map(
			("01", "Tiếng Việt"), ("02", "Tiếng Anh"), ("03", "Tiếng Pháp"), ("04", "Tiếng Nga"),
			("05", "Tiếng Trung"), ("06", "Việt Anh"), ("07", "Việt Nga"), ("08", "Việt Pháp"),
			("09", "Hán Nôm"), ("10", "Việt Trung"), ("11", "Khác")),
		["confidenceLevel"] = Map(("01", "Gốc điện tử"), ("02", "Số hóa"), ("03", "Hỗn hợp")),
		["format"] = Map(("01", "Tốt"), ("02", "Bình thường"), ("03", "Hỏng")),
		["riskRecovery"] = Map(("0", "Không"), ("1", "Có")),
		["riskRecoveryStatus"] = Map(("01", "Đã dự phòng"), ("02", "Chưa dự phòng")),
		["process"] = Map(("0", "Không"), ("0", "Không có quy trình xử lý đi kèm"), ("1", "Có"), ("1", "Có quy trình xử lý đi kèm")),
		["docAttached"] = Map(("0", "Không"), ("0", "Không có tài liệu đi kèm"), ("1", "Có"), ("1", "Có tài liệu đi kèm")),
		["typePic"] = Map(("01", "Phim âm bản"), ("02", "Ảnh")),
		["typeMedia"] = Map(("01", "Ghi âm"), ("02", "Ghi hình")),
		["colour"] = Map(("01", "Màu"), ("02", "Đen trắng")),
		["typeName"] = Map(
			("01", "Nghị quyết"), ("02", "Quyết định"), ("03", "Chỉ thị"), ("04", "Quy chế"),
			("05", "Quy định"), ("06", "Thông cáo"), ("07", "Thông báo"), ("08", "Hướng dẫn"),
			("09", "Chương trình"), ("10", "Kế hoạch"), ("11", "Phương án"), ("12", "Đề án"),
			("13", "Dự án"), ("14", "Báo cáo"), ("15", "Tờ trình"), ("16", "Giấy ủy quyền"),
			("17", "Phiếu gửi"), ("18", "Phiếu chuyển"), ("19", "Phiếu báo"), ("20", "Biên bản"),
			("21", "Hợp đồng"), ("22", "Công văn"), ("23", "Công điện"), ("24", "Bản ghi nhớ"),
			("25", "Bản thỏa thuận"), ("26", "Giấy mời"), ("27", "Giấy giới thiệu"),
			("28", "Giấy nghỉ phép"), ("29", "Thư công"), ("30", "Bản đồ"),
			("31", "Bản vẽ kỹ thuật"), ("32", "Khác")),
		["loaiTaiLieu"] = Map(
			("01", "Nghị quyết"), ("02", "Quyết định"), ("03", "Chỉ thị"), ("04", "Quy chế"),
			("05", "Quy định"), ("06", "Thông cáo"), ("07", "Thông báo"), ("08", "Hướng dẫn"),
			("09", "Chương trình"), ("10", "Kế hoạch"), ("11", "Phương án"), ("12", "Đề án"),
			("13", "Dự án"), ("14", "Báo cáo"), ("15", "Tờ trình"), ("16", "Giấy ủy quyền"),
			("17", "Phiếu gửi"), ("18", "Phiếu chuyển"), ("19", "Phiếu báo"), ("20", "Biên bản"),
			("21", "Hợp đồng"), ("22", "Công văn"), ("23", "Công điện"), ("24", "Bản ghi nhớ"),
			("25", "Bản thỏa thuận"), ("26", "Giấy mời"), ("27", "Giấy giới thiệu"),
			("28", "Giấy nghỉ phép"), ("29", "Thư công"), ("30", "Bản đồ"),
			("31", "Bản vẽ kỹ thuật"), ("32", "Khác")),
		["purpose"] = Map(("01", "Cá nhân"), ("02", "Công vụ"), ("03", "Công vụ đặc biệt")),
		["mucDich"] = Map(("01", "Cá nhân"), ("02", "Công vụ"), ("03", "Công vụ đặc biệt")),
		["feeObjectType"] = Map(
			("01", "Học sinh/sinh viên/học viên/nghiên cứu sinh"), ("02", "Thân nhân liệt sĩ"),
			("03", "Thương binh, bệnh binh"), ("04", "Người hoạt động kháng chiến"),
			("05", "Người có công giúp đỡ cách mạng"), ("06", "Người thờ cúng liệt sĩ"),
			("07", "Người hưởng chế độ hưu trí"), ("08", "Người mất sức lao động, tai nạn lao động"),
			("09", "Người bị mắc bệnh nghề nghiệp"), ("10", "Khác"))
	};

	public static string Convert(string fieldName, string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return value ?? string.Empty;

		string field = fieldName.Trim();
		if (!CodeLists.TryGetValue(field, out Dictionary<string, string>? list))
			return value.Trim();

		string trimmed = value.Trim();
		if (field.Equals("language", StringComparison.OrdinalIgnoreCase))
		{
			string[] parts = trimmed.Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length > 1)
				return string.Join(",", parts.Select(part => ConvertSingle(list, part)));
		}
		return ConvertSingle(list, trimmed);
	}

	private static string ConvertSingle(Dictionary<string, string> codeList, string value)
	{
		string normalized = Normalize(value);
		foreach (KeyValuePair<string, string> item in codeList)
			if (Normalize(item.Key) == normalized || Normalize(item.Value) == normalized)
				return item.Value;
		if (value.Length == 1 && char.IsDigit(value[0]))
		{
			string paddedCode = "0" + value;
			if (codeList.Values.Contains(paddedCode, StringComparer.OrdinalIgnoreCase))
				return paddedCode;
		}

		// Keep unrecognized free text intact. The package validator will report an
		// invalid controlled value rather than silently guessing a mapping.
		return value.Trim();
	}

	private static Dictionary<string, string> Map(params (string Code, string Label)[] entries)
	{
		Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string code, string label) in entries)
			result[label] = code;
		return result;
	}

	private static string Normalize(string value)
	{
		string decomposed = value.Trim().Normalize(NormalizationForm.FormD);
		StringBuilder result = new(decomposed.Length);
		foreach (char character in decomposed)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
				continue;
			if (char.IsLetterOrDigit(character))
				result.Append(char.ToLowerInvariant(character));
		}
		return result.ToString();
	}
}
