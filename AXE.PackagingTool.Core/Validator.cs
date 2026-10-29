using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AXE.PackagingTool.Models;

namespace AXE.PackagingTool.Core;

public class Validator
{
	private readonly string _baseFolder;

	private readonly Action<string> _log;

	private static readonly HashSet<string> ValidExtVanBan = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };

	private static readonly HashSet<string> ValidExtAnhPhim = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".tiff", ".tif", ".png", ".pdf" };

	private static readonly HashSet<string> ValidExtGhiAmVideo = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wma", ".mp4", ".avi", ".wmv", ".wav" };

	public Validator(string baseFolder, Action<string> log)
	{
		_baseFolder = baseFolder;
		_log = log;
	}

	public List<ValidationIssue> Validate(PackageData data)
	{
		List<ValidationIssue> list = new List<ValidationIssue>();
		foreach (FondModel fond in data.Fonds)
		{
			ValidateFond(fond, list);
			foreach (CaseFileModel caseFile in fond.CaseFiles)
			{
				ValidateCaseFile(caseFile, fond.FondCode, list);
			}
			foreach (DocumentModel item in fond.CaseFiles.SelectMany((CaseFileModel cf) => cf.Documents).Concat(fond.LooseDocuments))
			{
				ValidateDocument(item, list);
			}
		}
		if (data.PackageType == PackageType.DIP && data.DipXacThuc != null)
		{
			ValidateDipXacThuc(data.DipXacThuc, list);
		}
		int value = list.Count((ValidationIssue i) => i.Severity == "ERROR");
		int value2 = list.Count((ValidationIssue i) => i.Severity == "WARNING");
		_log($"  Kết quả validate: {value} lỗi | {value2} cảnh báo");
		return list;
	}

	private static void ValidateFond(FondModel fond, List<ValidationIssue> issues)
	{
		RequiredStr(fond.FondCode, "PHONG", 0, "MaPhong", issues);
		RequiredStr(fond.FondName, "PHONG", 0, "TenPhong", issues);
		RequiredStr(fond.FondHistory, "PHONG", 0, "LichSu", issues);
		RequiredStr(fond.DocumentDateFrom, "PHONG", 0, "ThoiGianBD", issues);
		if (!string.IsNullOrEmpty(fond.Status) && fond.Status != "mở" && fond.Status != "đóng")
		{
			Warn("PHONG", 0, "TrangThai '" + fond.Status + "' không hợp lệ (mở/đóng)", issues);
		}
	}

	private static void ValidateCaseFile(CaseFileModel cf, string fondCode, List<ValidationIssue> issues)
	{
		RequiredStr(cf.CaseFileCode, "HOSO", 0, "MaHoSo", issues);
		RequiredStr(cf.CaseFileTitle, "HOSO", 0, "TieuDe", issues);
		RequiredStr(cf.RetentionPeriod, "HOSO", 0, "ThoiHanLuu", issues);
		RequiredDate(cf.DateFrom, "HOSO", 0, "ThoiGianBD", issues);
		RequiredDate(cf.DateTo, "HOSO", 0, "ThoiGianKT", issues);
		ValidateAccessLevel(cf.AccessLevel, "HOSO", 0, issues);
		if (cf.PageCount <= 0)
		{
			Warn("HOSO", 0, "Hồ sơ '" + cf.CaseFileCode + "': SoTrang nên > 0", issues);
		}
	}

	private void ValidateDocument(DocumentModel doc, List<ValidationIssue> issues)
	{
		string sheet = doc.Category switch
		{
			DocumentCategory.VanBanHanhChinh => "TAILIEU_VANBANHANHCHINH", 
			DocumentCategory.AnhPhimAmBan => "TAILIEU_ANHANH", 
			DocumentCategory.GhiAmVideo => "TAILIEU_GHIAM_VIDEO", 
			_ => "TAILIEU", 
		};
		RequiredStr(doc.ArchivalCode, sheet, 0, "MaLuuTru", issues);
		RequiredStr(doc.FileAttachment, sheet, 0, "TepTin", issues);
		ValidateAccessLevel(doc.AccessLevel, sheet, 0, issues);
		if (!string.IsNullOrEmpty(doc.FileAttachment))
		{
			string text = (doc.ResolvedSourcePath = (string.IsNullOrEmpty(doc.Path) ? Path.Combine(_baseFolder, doc.FileAttachment) : Path.Combine(_baseFolder, doc.Path)));
			if (!File.Exists(text))
			{
				Error(sheet, 0, $"File không tìm thấy: {text} (TepTin={doc.FileAttachment})", issues);
			}
			else
			{
				string extension = Path.GetExtension(text);
				if (!(doc.Category switch
				{
					DocumentCategory.VanBanHanhChinh => ValidExtVanBan, 
					DocumentCategory.AnhPhimAmBan => ValidExtAnhPhim, 
					DocumentCategory.GhiAmVideo => ValidExtGhiAmVideo, 
					_ => ValidExtVanBan, 
				}).Contains(extension))
				{
					Warn(sheet, 0, $"Extension '{extension}' có thể không hợp lệ cho loại {doc.Category}. File: {doc.FileAttachment}", issues);
				}
			}
		}
		if (doc is VanBanModel vanBanModel)
		{
			RequiredStr(vanBanModel.DocumentUID, sheet, 0, "MăĐịnhDanh", issues);
			RequiredStr(vanBanModel.DocumentType, sheet, 0, "TênLoạiTaiLieu", issues);
			RequiredStr(vanBanModel.IssuingAuthority, sheet, 0, "CoQuan", issues);
			RequiredStr(vanBanModel.Summary, sheet, 0, "TrichYeu", issues);
			RequiredStr(vanBanModel.Language, sheet, 0, "NgonNgu", issues);
			RequiredStr(vanBanModel.RetentionPeriod, sheet, 0, "ThoiHanLuu", issues);
			RequiredDateField(vanBanModel.IssuedDate, sheet, 0, "NgayBanHanh", issues);
			if (vanBanModel.PageCount <= 0)
			{
				Error(sheet, 0, "Tài liệu '" + vanBanModel.ArchivalCode + "': SốLượngTrang phải > 0 (bắt buộc)", issues);
			}
		}
		else if (doc is AnhPhimModel anhPhimModel)
		{
			RequiredStr(anhPhimModel.RetentionPeriod, sheet, 0, "ThoiHanLuu", issues);
			RequiredStr(anhPhimModel.MediaType, sheet, 0, "PhanLoai (am ban goc/duong ban)", issues);
			RequiredStr(anhPhimModel.ArchivalNumber, sheet, 0, "SoLuuTru", issues);
			RequiredStr(anhPhimModel.EventName, sheet, 0, "TenSuKien", issues);
			RequiredStr(anhPhimModel.Title, sheet, 0, "TieuDe", issues);
			RequiredStr(anhPhimModel.Author, sheet, 0, "TacGia", issues);
			RequiredStr(anhPhimModel.Location, sheet, 0, "DiaDiem", issues);
			RequiredStr(anhPhimModel.ColorType, sheet, 0, "MauSac", issues);
			RequiredStr(anhPhimModel.Size, sheet, 0, "CoPhin", issues);
			RequiredDateField(anhPhimModel.CaptureDate, sheet, 0, "ThoiGianChup", issues);
		}
		else if (doc is GhiAmVideoModel ghiAmVideoModel)
		{
			RequiredStr(ghiAmVideoModel.RetentionPeriod, sheet, 0, "ThoiHanLuu", issues);
			RequiredStr(ghiAmVideoModel.MediaCategory, sheet, 0, "PhanLoai (am thanh/video)", issues);
			RequiredStr(ghiAmVideoModel.ArchivalNumber, sheet, 0, "SoLuuTru", issues);
			RequiredStr(ghiAmVideoModel.EventName, sheet, 0, "TenSuKien", issues);
			RequiredStr(ghiAmVideoModel.Title, sheet, 0, "TieuDe", issues);
			RequiredStr(ghiAmVideoModel.Author, sheet, 0, "TacGia", issues);
			RequiredStr(ghiAmVideoModel.Location, sheet, 0, "DiaDiem", issues);
			RequiredStr(ghiAmVideoModel.Language, sheet, 0, "NgonNgu", issues);
			RequiredStr(ghiAmVideoModel.Duration, sheet, 0, "ThoiLuong", issues);
			RequiredDateField(ghiAmVideoModel.RecordingDate, sheet, 0, "ThoiGianGhiAm", issues);
		}
	}

	private static void ValidateDipXacThuc(DipXacThucModel dip, List<ValidationIssue> issues)
	{
		RequiredStr(dip.MaXacThuc, "DIP_XACTHUC", 2, "MaXacThuc", issues);
		RequiredStr(dip.TenLuuTruLichSu, "DIP_XACTHUC", 2, "TenLuuTru", issues);
		RequiredStr(dip.HoTenNguoiNhan, "DIP_XACTHUC", 2, "HoTen", issues);
		RequiredStr(dip.MaDinhDanhCongDan, "DIP_XACTHUC", 2, "MaDinhDanh", issues);
		RequiredStr(dip.MaLuuTruGoc, "DIP_XACTHUC", 2, "MaLuuTruGoc", issues);
		RequiredStr(dip.TenLoaiTaiLieu, "DIP_XACTHUC", 2, "TenLoaiTaiLieu", issues);
		RequiredStr(dip.TrichYeu, "DIP_XACTHUC", 2, "TrichYeu", issues);
		RequiredStr(dip.MucDichSuDung, "DIP_XACTHUC", 2, "MucDich", issues);
		RequiredStr(dip.ThoiHanSuDung, "DIP_XACTHUC", 2, "ThoiHanSuDung", issues);
		RequiredDateField(dip.NgayCap, "DIP_XACTHUC", 2, "NgayCap", issues);
		if (dip.SoLuongBan <= 0)
		{
			Error("DIP_XACTHUC", 2, "SoLuongBan phải > 0", issues);
		}
	}

	private static void RequiredStr(string value, string sheet, int row, string field, List<ValidationIssue> issues)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			Error(sheet, row, "Trường bắt buộc '" + field + "' đang bỏ trống", issues);
		}
	}

	private static void RequiredDate(string value, string sheet, int row, string field, List<ValidationIssue> issues)
	{
		if (!string.IsNullOrWhiteSpace(value) && !DateTime.TryParse(value, out var _))
		{
			Error(sheet, row, $"'{field}' = '{value}' không phải định dạng ngày hợp lệ (YYYY-MM-DD)", issues);
		}
	}

	private static void RequiredDateField(string value, string sheet, int row, string field, List<ValidationIssue> issues)
	{
		DateTime result;
		if (string.IsNullOrWhiteSpace(value))
		{
			Error(sheet, row, "Trưᤁng bắt buộc '" + field + "' đang bỏ trống", issues);
		}
		else if (!DateTime.TryParse(value, out result))
		{
			Error(sheet, row, $"'{field}' = '{value}' không phải định dạng ngày hợp lệ (YYYY-MM-DD)", issues);
		}
	}

	private static void ValidateAccessLevel(string value, string sheet, int row, List<ValidationIssue> issues)
	{
		string[] array = new string[4] { "Công khai", "Hạn chế", "Mật", "Tuyệt mật" };
		if (string.IsNullOrWhiteSpace(value))
		{
			Error(sheet, row, "Trưᤁng bắt buộc 'MucDoTiepCan' đang bỏ trống", issues);
		}
		else if (!array.Contains(value))
		{
			Error(sheet, row, "MucDoTiepCan '" + value + "' không nằm trong danh sách chuẩn (Công khai/Hạn chế/Mật/Tuyệt mật)", issues);
		}
	}

	private static void Error(string sheet, int row, string msg, List<ValidationIssue> issues)
	{
		issues.Add(new ValidationIssue
		{
			Severity = "ERROR",
			Sheet = sheet,
			Row = row,
			Message = msg
		});
	}

	private static void Warn(string sheet, int row, string msg, List<ValidationIssue> issues)
	{
		issues.Add(new ValidationIssue
		{
			Severity = "WARNING",
			Sheet = sheet,
			Row = row,
			Message = msg
		});
	}
}
