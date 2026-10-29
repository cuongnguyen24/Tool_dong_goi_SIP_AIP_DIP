using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AXE.PackagingTool.Models;
using ClosedXML.Excel;

namespace AXE.PackagingTool.Core;

public class ExcelReader
{
	private readonly Action<string> _log;

	public ExcelReader(Action<string> log)
	{
		_log = log;
	}

	public PackageData Read(string filePath, PackageType packageType)
	{
		_log("\ud83d\udcc2 Đọc file Excel: " + Path.GetFileName(filePath));
		using XLWorkbook wb = new XLWorkbook(filePath);
		List<FondModel> list = ReadFonds(wb);
		List<CaseFileModel> list2 = ReadCaseFiles(wb);
		List<VanBanModel> list3 = ReadVanBan(wb);
		List<AnhPhimModel> list4 = ReadAnhPhim(wb);
		List<GhiAmVideoModel> list5 = ReadGhiAmVideo(wb);
		Dictionary<(string, string), CaseFileModel> dictionary = list2.ToDictionary((CaseFileModel cf) => (FondCode: cf.FondCode, CaseFileCode: cf.CaseFileCode), (CaseFileModel cf) => cf);
		foreach (DocumentModel doc in list3.Cast<DocumentModel>().Concat(list4).Concat(list5))
		{
			if (!string.IsNullOrWhiteSpace(doc.CaseFileCode) && dictionary.TryGetValue((doc.FondCode, doc.CaseFileCode), out var value))
			{
				value.Documents.Add(doc);
				continue;
			}
			list.FirstOrDefault((FondModel f) => f.FondCode == doc.FondCode)?.LooseDocuments.Add(doc);
		}
		Dictionary<string, FondModel> dictionary2 = list.ToDictionary((FondModel f) => f.FondCode);
		foreach (CaseFileModel item in list2)
		{
			if (dictionary2.TryGetValue(item.FondCode, out var value2))
			{
				value2.CaseFiles.Add(item);
			}
		}
		_log($"✅ Đọc xong: {list.Count} phông | {list2.Count} hồ sơ | {list3.Count + list4.Count + list5.Count} tài liệu");
		PackageData packageData = new PackageData
		{
			PackageType = packageType,
			Fonds = list
		};
		if (packageType == PackageType.DIP)
		{
			packageData.DipXacThuc = ReadDipXacThuc(wb);
		}
		return packageData;
	}

	private List<FondModel> ReadFonds(XLWorkbook wb)
	{
		if (!wb.TryGetWorksheet("PHONG", out var worksheet))
		{
			throw new InvalidDataException("Không tìm thấy sheet 'PHONG' trong file Excel.");
		}
		Dictionary<string, int> map = BuildColumnMap(worksheet);
		List<FondModel> list = new List<FondModel>();
		foreach (int item in DataRows(worksheet))
		{
			list.Add(new FondModel
			{
				FondCode = Str(worksheet, item, map, "MaPhong"),
				FondName = Str(worksheet, item, map, "TenPhong"),
				Status = Str(worksheet, item, map, "TrangThai"),
				FondHistory = Str(worksheet, item, map, "LichSu"),
				DocumentDateFrom = Str(worksheet, item, map, "ThoiGianBD"),
				DocumentDateTo = Str(worksheet, item, map, "ThoiGianKT"),
				TotalDocuments = Int(worksheet, item, map, "TongSo"),
				ClassificationScheme = Str(worksheet, item, map, "PhanLoai"),
				Language = Str(worksheet, item, map, "NgonNgu"),
				BackupMode = Str(worksheet, item, map, "CheDoDuPhong"),
				Note = Str(worksheet, item, map, "GhiChu")
			});
		}
		_log($"  • PHONG: {list.Count} bản ghi");
		return list;
	}

	private List<CaseFileModel> ReadCaseFiles(XLWorkbook wb)
	{
		if (!wb.TryGetWorksheet("HOSO", out var worksheet))
		{
			return new List<CaseFileModel>();
		}
		Dictionary<string, int> map = BuildColumnMap(worksheet);
		List<CaseFileModel> list = new List<CaseFileModel>();
		foreach (int item in DataRows(worksheet))
		{
			list.Add(new CaseFileModel
			{
				FondCode = Str(worksheet, item, map, "MaPhong"),
				CaseFileCode = Str(worksheet, item, map, "MaHoSo"),
				CaseFileTitle = Str(worksheet, item, map, "TieuDe"),
				RetentionPeriod = Str(worksheet, item, map, "ThoiHanLuu"),
				AccessLevel = Str(worksheet, item, map, "MucDoTiepCan"),
				Language = Str(worksheet, item, map, "NgonNgu"),
				DateFrom = Str(worksheet, item, map, "ThoiGianBD"),
				DateTo = Str(worksheet, item, map, "ThoiGianKT"),
				Keywords = Str(worksheet, item, map, "TuKhoa"),
				TotalDocuments = Int(worksheet, item, map, "TongSo"),
				SheetCount = Int(worksheet, item, map, "SoTo"),
				PageCount = Int(worksheet, item, map, "SoTrang"),
				PhysicalCondition = Str(worksheet, item, map, "TinhTrangVatLy"),
				InformationSymbol = Str(worksheet, item, map, "KyHieuThongTin"),
				ReliabilityLevel = Str(worksheet, item, map, "MucDoTinCay"),
				OriginalPaperCode = Str(worksheet, item, map, "MaGoc"),
				BackupMode = Str(worksheet, item, map, "CheDoDuPhong"),
				BackupStatus = Str(worksheet, item, map, "TinhTrangDuPhong"),
				CaseFileAttachment = Str(worksheet, item, map, "TepTin"),
				Note = Str(worksheet, item, map, "GhiChu")
			});
		}
		_log($"  • HOSO: {list.Count} bản ghi");
		return list;
	}

	private List<VanBanModel> ReadVanBan(XLWorkbook wb)
	{
		if (!wb.TryGetWorksheet("TAILIEU_VANBANHANHCHINH", out var worksheet))
		{
			return new List<VanBanModel>();
		}
		Dictionary<string, int> map = BuildColumnMap(worksheet);
		List<VanBanModel> list = new List<VanBanModel>();
		foreach (int item in DataRows(worksheet))
		{
			list.Add(new VanBanModel
			{
				FondCode = Str(worksheet, item, map, "MaPhong"),
				CaseFileCode = Str(worksheet, item, map, "MaHoSo"),
				DocumentUID = Str(worksheet, item, map, "MaDinhDanh"),
				ArchivalCode = Str(worksheet, item, map, "MaLuuTru"),
				RetentionPeriod = Str(worksheet, item, map, "ThoiHanLuu"),
				DocumentType = Str(worksheet, item, map, "LoaiTaiLieu"),
				DocumentNumber = Str(worksheet, item, map, "So"),
				DocumentSymbol = Str(worksheet, item, map, "KyHieu"),
				IssuedDate = Str(worksheet, item, map, "NgayBanHanh"),
				IssuingAuthority = Str(worksheet, item, map, "CoQuan"),
				Summary = Str(worksheet, item, map, "TrichYeu"),
				Language = Str(worksheet, item, map, "NgonNgu"),
				PageCount = Int(worksheet, item, map, "SoTrang"),
				SheetCount = Int(worksheet, item, map, "SoTo"),
				InformationSymbol = Str(worksheet, item, map, "KyHieuThongTin"),
				Keywords = Str(worksheet, item, map, "TuKhoa"),
				AccessLevel = Str(worksheet, item, map, "MucDoTiepCan"),
				ReliabilityLevel = Str(worksheet, item, map, "MucDoTinCay"),
				HandwrittenAnnotation = Str(worksheet, item, map, "ButTich"),
				PhysicalCondition = Str(worksheet, item, map, "TinhTrangVatLy"),
				ProcessingWorkflow = Str(worksheet, item, map, "QuyTrinh"),
				BackupMode = Str(worksheet, item, map, "CheDoDuPhong"),
				BackupStatus = Str(worksheet, item, map, "TinhTrangDuPhong"),
				FileAttachment = Str(worksheet, item, map, "TepTin"),
				Path = Str(worksheet, item, map, "Path"),
				Note = Str(worksheet, item, map, "GhiChu")
			});
		}
		_log($"  • TAILIEU_VANBANHANHCHINH: {list.Count} bản ghi");
		return list;
	}

	private List<AnhPhimModel> ReadAnhPhim(XLWorkbook wb)
	{
		if (!wb.TryGetWorksheet("TAILIEU_ANHANH", out var worksheet))
		{
			return new List<AnhPhimModel>();
		}
		Dictionary<string, int> map = BuildColumnMap(worksheet);
		List<AnhPhimModel> list = new List<AnhPhimModel>();
		foreach (int item in DataRows(worksheet))
		{
			list.Add(new AnhPhimModel
			{
				FondCode = Str(worksheet, item, map, "MaPhong"),
				CaseFileCode = Str(worksheet, item, map, "MaHoSo"),
				ArchivalCode = Str(worksheet, item, map, "MaLuuTru"),
				RetentionPeriod = Str(worksheet, item, map, "ThoiHanLuu"),
				MediaType = Str(worksheet, item, map, "PhanLoai"),
				ArchivalNumber = Str(worksheet, item, map, "SoLuuTru"),
				InformationSymbol = Str(worksheet, item, map, "KyHieuThongTin"),
				EventName = Str(worksheet, item, map, "TenSuKien"),
				Title = Str(worksheet, item, map, "TieuDe"),
				Author = Str(worksheet, item, map, "TacGia"),
				Location = Str(worksheet, item, map, "DiaDiem"),
				CaptureDate = Str(worksheet, item, map, "ThoiGianChup"),
				ColorType = Str(worksheet, item, map, "MauSac"),
				Size = Str(worksheet, item, map, "Co"),
				AttachedDocuments = Str(worksheet, item, map, "TaiLieuDiKem"),
				AccessLevel = Str(worksheet, item, map, "MucDoTiepCan"),
				PhysicalCondition = Str(worksheet, item, map, "TinhTrangVatLy"),
				BackupMode = Str(worksheet, item, map, "CheDoDuPhong"),
				BackupStatus = Str(worksheet, item, map, "TinhTrangDuPhong"),
				FileAttachment = Str(worksheet, item, map, "TepTin"),
				Path = Str(worksheet, item, map, "Path"),
				Note = Str(worksheet, item, map, "GhiChu")
			});
		}
		_log($"  • TAILIEU_ANHANH: {list.Count} bản ghi");
		return list;
	}

	private List<GhiAmVideoModel> ReadGhiAmVideo(XLWorkbook wb)
	{
		if (!wb.TryGetWorksheet("TAILIEU_GHIAM_VIDEO", out var worksheet))
		{
			return new List<GhiAmVideoModel>();
		}
		Dictionary<string, int> map = BuildColumnMap(worksheet);
		List<GhiAmVideoModel> list = new List<GhiAmVideoModel>();
		foreach (int item in DataRows(worksheet))
		{
			list.Add(new GhiAmVideoModel
			{
				FondCode = Str(worksheet, item, map, "MaPhong"),
				CaseFileCode = Str(worksheet, item, map, "MaHoSo"),
				ArchivalCode = Str(worksheet, item, map, "MaLuuTru"),
				RetentionPeriod = Str(worksheet, item, map, "ThoiHanLuu"),
				MediaCategory = Str(worksheet, item, map, "PhanLoai"),
				ArchivalNumber = Str(worksheet, item, map, "SoLuuTru"),
				InformationSymbol = Str(worksheet, item, map, "KyHieuThongTin"),
				EventName = Str(worksheet, item, map, "TenSuKien"),
				Title = Str(worksheet, item, map, "TieuDe"),
				Author = Str(worksheet, item, map, "TacGia"),
				Location = Str(worksheet, item, map, "DiaDiem"),
				RecordingDate = Str(worksheet, item, map, "ThoiGian"),
				Language = Str(worksheet, item, map, "NgonNgu"),
				Duration = Str(worksheet, item, map, "ThoiLuong"),
				AttachedDocuments = Str(worksheet, item, map, "TaiLieuDiKem"),
				AccessLevel = Str(worksheet, item, map, "MucDoTiepCan"),
				QualityLevel = Str(worksheet, item, map, "ChatLuong"),
				PhysicalCondition = Str(worksheet, item, map, "TinhTrangVatLy"),
				BackupMode = Str(worksheet, item, map, "CheDoDuPhong"),
				BackupStatus = Str(worksheet, item, map, "TinhTrangDuPhong"),
				FileAttachment = Str(worksheet, item, map, "TepTin"),
				Path = Str(worksheet, item, map, "Path"),
				Note = Str(worksheet, item, map, "GhiChu")
			});
		}
		_log($"  • TAILIEU_GHIAM_VIDEO: {list.Count} bản ghi");
		return list;
	}

	private DipXacThucModel? ReadDipXacThuc(XLWorkbook wb)
	{
		if (!wb.TryGetWorksheet("DIP_XACTHUC", out var worksheet))
		{
			return null;
		}
		Dictionary<string, int> map = BuildColumnMap(worksheet);
		List<int> list = DataRows(worksheet).ToList();
		if (list.Count == 0)
		{
			return null;
		}
		int row = list.First();
		return new DipXacThucModel
		{
			MaXacThuc = Str(worksheet, row, map, "MaXacThuc"),
			TenLuuTruLichSu = Str(worksheet, row, map, "TenLuuTru"),
			HoTenNguoiNhan = Str(worksheet, row, map, "HoTen"),
			MaDinhDanhCongDan = Str(worksheet, row, map, "MaDinhDanh"),
			MaLuuTruGoc = Str(worksheet, row, map, "MaLuuTruGoc"),
			SoKyHieuGoc = Str(worksheet, row, map, "SoKyHieu"),
			TenLoaiTaiLieu = Str(worksheet, row, map, "LoaiTaiLieu"),
			TrichYeu = Str(worksheet, row, map, "TrichYeu"),
			MucDichSuDung = Str(worksheet, row, map, "MucDich"),
			NgayCap = Str(worksheet, row, map, "NgayCap"),
			ThoiHanSuDung = Str(worksheet, row, map, "ThoiHan"),
			SoLuongBan = Int(worksheet, row, map, "SoLuong")
		};
	}

	private static Dictionary<string, int> BuildColumnMap(IXLWorksheet ws)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		int num = ws.LastColumnUsed()?.ColumnNumber() ?? 50;
		for (int i = 1; i <= num; i++)
		{
			string text = ws.Cell(1, i).GetString().Trim();
			if (!string.IsNullOrEmpty(text))
			{
				string key = NormalizeHeader(text);
				if (!dictionary.ContainsKey(key))
				{
					dictionary[key] = i;
				}
			}
		}
		return dictionary;
	}

	private static string NormalizeHeader(string header)
	{
		return Regex.Replace(header.ToUpperInvariant().Normalize(NormalizationForm.FormD), "[^A-Z0-9]", "");
	}

	private static IEnumerable<int> DataRows(IXLWorksheet ws)
	{
		int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
		for (int r = 3; r <= lastRow; r++)
		{
			if (!ws.Row(r).IsEmpty())
			{
				yield return r;
			}
		}
	}

	private static string Str(IXLWorksheet ws, int row, Dictionary<string, int> map, string key)
	{
		if (!map.TryGetValue(key.ToUpperInvariant(), out var value))
		{
			return "";
		}
		return ws.Cell(row, value).GetString().Trim();
	}

	private static int Int(IXLWorksheet ws, int row, Dictionary<string, int> map, string key)
	{
		if (!map.TryGetValue(key.ToUpperInvariant(), out var value))
		{
			return 0;
		}
		if (!int.TryParse(ws.Cell(row, value).GetString().Trim(), out var result))
		{
			return 0;
		}
		return result;
	}
}
