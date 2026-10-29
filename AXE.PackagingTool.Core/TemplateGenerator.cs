using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using AXE.PackagingTool.Models;
using ClosedXML.Excel;
using ExcelDataReader;

namespace AXE.PackagingTool.Core;

public static class TemplateGenerator
{
	private static readonly XLColor ColHeader = XLColor.FromHtml("#1F4E79");

	private static readonly XLColor ColRequired = XLColor.FromHtml("#FFDDC1");

	private static readonly XLColor ColOptional = XLColor.FromHtml("#EBF5FB");

	private static readonly XLColor ColPath = XLColor.FromHtml("#D5F5E3");

	private static readonly XLColor ColInfo = XLColor.FromHtml("#F9F9F9");

	public static void Generate(PackageType type, string outputPath)
	{
		using XLWorkbook xLWorkbook = new XLWorkbook();
		Dictionary<string, string> categories = LoadCategories();
		AddInstructionSheet(xLWorkbook, type);
		AddPhongSheet(xLWorkbook, categories);
		switch (type)
		{
		case PackageType.AIP:
			AddHoSoSheet(xLWorkbook, categories);
			AddVanBanSheet(xLWorkbook, categories);
			AddAnhPhimSheet(xLWorkbook, categories);
			AddGhiAmVideoSheet(xLWorkbook, categories);
			break;
		case PackageType.SIP:
			AddHoSoSheet(xLWorkbook, categories);
			AddVanBanSheet(xLWorkbook, categories);
			AddAnhPhimSheet(xLWorkbook, categories);
			AddGhiAmVideoSheet(xLWorkbook, categories);
			AddMucLucSheet(xLWorkbook);
			break;
		case PackageType.DIP:
			AddVanBanSheet(xLWorkbook, categories);
			AddAnhPhimSheet(xLWorkbook, categories);
			AddGhiAmVideoSheet(xLWorkbook, categories);
			AddDipXacThucSheet(xLWorkbook);
			break;
		}
		xLWorkbook.SaveAs(outputPath);
	}

	private static void AddInstructionSheet(XLWorkbook wb, PackageType type)
	{
		IXLWorksheet iXLWorksheet = wb.Worksheets.Add("HUONG_DAN");
		iXLWorksheet.TabColor = XLColor.Orange;
		iXLWorksheet.Cell("A1").Value = "HƯỚNG DẪN SỬ DỤNG — Template " + type.ToDisplayName();
		iXLWorksheet.Cell("A1").Style.Font.Bold = true;
		iXLWorksheet.Cell("A1").Style.Font.FontSize = 14.0;
		iXLWorksheet.Cell("A1").Style.Font.FontColor = XLColor.DarkRed;
		int num = 3;
		string[] array = new string[28]
		{
			"CẤU TRÚC HEADER (2 DÒNG ĐẦU):", "  Dòng 1 — Mã trường (code): tên kỹ thuật ngắn, dùng để phần mềm nhận diện cột. KHÔNG xóa hoặc sửa.", "  Dòng 2 — Tên trường (mô tả): tên tiếng Việt đầy đủ, chỉ để đọc. Không dùng để xử lý.", "  Dữ liệu điền từ DÒNG 3 trở đi.", "", "MÀU NỀN CỘT:", "  Nền CAM NHẠT  (★) = BẮT BUỘC phải điền.", "  Nền XANH NHẠT     = Tùy chọn, có thể để trống.", "  Nền XANH LÁ       = Đường dẫn file (Path) — xem hướng dẫn phía dưới.", "",
			"CỘT PATH (đường dẫn tương đối đến file nguồn):", "  Nhập đường dẫn TƯƠNG ĐỐI từ Thư mục gốc đến file tài liệu.", "  Ví dụ: Thư mục gốc = D:\\TaiLieu\\G09", "          File nằm ở  = D:\\TaiLieu\\G09\\QuiII\\QD123.pdf", "          → Cột Path  = QuiII\\QD123.pdf", "  Nếu file nằm ngay trong Thư mục gốc → để trống cột Path.", "", "CỘT TepTin:", "  Tên file đích sẽ lưu trong gói ZIP (không có đường dẫn, chỉ là tên file).", "  Ví dụ: QD123.pdf",
			"", "LIÊN KẾT DỮ LIỆU GIỮA CÁC SHEET:", "  Cột MaPhong trong sheet HOSO và TAILIEU_* phải khớp với sheet PHONG.", "  Cột MaHoSo trong sheet TAILIEU_* phải khớp với sheet HOSO (nếu có hồ sơ).", "", "ĐỊNH DẠNG NGÀY THÁNG:", "  Nhập theo dạng YYYY-MM-DD, ví dụ: 2024-03-15", "  Cũng chấp nhận: YYYY-MM hoặc YYYY (cho trường năm chỉ có năm)."
		};
		foreach (string text in array)
		{
			iXLWorksheet.Cell(num, 1).Value = text;
			if (text.StartsWith("CẤU TRÚC") || text.StartsWith("MÀU NỀN") || text.StartsWith("CỘT PATH") || text.StartsWith("CỘT TepTin") || text.StartsWith("LIÊN KẾT") || text.StartsWith("ĐỊNH DẠNG"))
			{
				iXLWorksheet.Cell(num, 1).Style.Font.Bold = true;
				iXLWorksheet.Cell(num, 1).Style.Font.FontColor = XLColor.DarkBlue;
			}
			num++;
		}
		iXLWorksheet.Column(1).Width = 90.0;
		iXLWorksheet.SheetView.FreezeRows(0);
	}

	private static void AddPhongSheet(XLWorkbook wb, Dictionary<string, string> categories)
	{
		IXLWorksheet iXLWorksheet = wb.Worksheets.Add("PHONG");
		iXLWorksheet.TabColor = XLColor.DarkBlue;
		(string, string, bool, string)[] cols = new(string, string, bool, string)[11]
		{
			("MaPhong", "Mã phông/công trình/sưu tập", true, "Mã duy nhất, VD: G09"),
			("TenPhong", "Tên phông/công trình/sưu tập", true, null),
			("TrangThai", "Trạng thái", true, "mở / đóng"),
			("LichSu", "Lịch sử đơn vị hình thành phông", true, null),
			("ThoiGianBD", "Thời gian tài liệu (bắt đầu)", true, "YYYY hoặc YYYY-MM-DD"),
			("ThoiGianKT", "Thời gian tài liệu (kết thúc)", false, "YYYY hoặc YYYY-MM-DD"),
			("TongSo", "Tổng số tài liệu", true, "Số nguyên"),
			("PhanLoai", "Phương án phân loại", true, null),
			("NgonNgu", "Ngôn ngữ", true, "ISO 639: vi, en, fr"),
			("CheDoDuPhong", "Chế độ dự phòng", false, null),
			("GhiChu", "Ghi chú", false, null)
		};
		ApplyHeaders(iXLWorksheet, cols);
		AddDropdown(iXLWorksheet, FindCol(cols, "TrangThai"), "mở,đóng");
		AddDropdown(iXLWorksheet, FindCol(cols, "NgonNgu"), GetCategoryItems(categories, "ngon ngu", "vi,en"));
		FreezeAndAutoFit(iXLWorksheet);
	}

	private static void AddHoSoSheet(XLWorkbook wb, Dictionary<string, string> categories)
	{
		IXLWorksheet iXLWorksheet = wb.Worksheets.Add("HOSO");
		iXLWorksheet.TabColor = XLColor.DarkGreen;
		(string, string, bool, string)[] cols = new(string, string, bool, string)[20]
		{
			("MaPhong", "Mã phông", true, "Khớp với sheet PHONG"),
			("MaHoSo", "Mã hồ sơ", true, "Mã duy nhất trong phông"),
			("TieuDe", "Tiêu đề hồ sơ", true, null),
			("ThoiHanLuu", "Thời hạn lưu trữ", true, "5 năm / 10 năm / Vĩnh viễn"),
			("MucDoTiepCan", "Mức độ tiếp cận", true, "Công khai / Hạn chế / Mật / Tuyệt mật"),
			("NgonNgu", "Ngôn ngữ", true, "vi / en"),
			("ThoiGianBD", "Thời gian bắt đầu", true, "YYYY-MM-DD"),
			("ThoiGianKT", "Thời gian kết thúc", true, "YYYY-MM-DD"),
			("TuKhoa", "Từ khóa", false, "Phân cách bằng ;"),
			("TongSo", "Tổng số tài liệu", true, "Số nguyên"),
			("SoTo", "Số lượng tờ (số hóa)", false, null),
			("SoTrang", "Số lượng trang", true, "Số nguyên"),
			("TinhTrangVatLy", "Tình trạng vật lý (số hóa)", false, "Tốt / Trung bình / Kém"),
			("KyHieuThongTin", "Ký hiệu thông tin", false, null),
			("MucDoTinCay", "Mức độ tin cậy", false, "Cao / Trung bình / Thấp"),
			("MaGoc", "Mã hồ sơ gốc giấy", false, "Chỉ với hồ sơ số hóa"),
			("CheDoDuPhong", "Chế độ lập TLLT dự phòng", false, null),
			("TinhTrangDuPhong", "Tình trạng lập TLLT dự phòng", false, null),
			("TepTin", "Tệp tin hồ sơ", false, "Tên file nếu toàn hồ sơ 1 file"),
			("GhiChu", "Ghi chú", false, null)
		};
		ApplyHeaders(iXLWorksheet, cols);
		AddDropdown(iXLWorksheet, FindCol(cols, "MucDoTiepCan"), GetCategoryItems(categories, "muc do tiep can", "Công khai,Hạn chế,Mật,Tuyệt mật"));
		AddDropdown(iXLWorksheet, FindCol(cols, "TinhTrangVatLy"), GetCategoryItems(categories, "tinh trang vat ly", "Tốt,Trung bình,Kém"));
		AddDropdown(iXLWorksheet, FindCol(cols, "MucDoTinCay"), GetCategoryItems(categories, "muc do tin cay", "Cao,Trung bình,Thấp"));
		AddDropdown(iXLWorksheet, FindCol(cols, "NgonNgu"), GetCategoryItems(categories, "ngon ngu", "vi,en"));
		FreezeAndAutoFit(iXLWorksheet);
	}

	private static void AddVanBanSheet(XLWorkbook wb, Dictionary<string, string> categories)
	{
		IXLWorksheet iXLWorksheet = wb.Worksheets.Add("TAILIEU_VANBANHANHCHINH");
		iXLWorksheet.TabColor = XLColor.DarkOrange;
		(string, string, bool, string)[] cols = new(string, string, bool, string)[26]
		{
			("MaPhong", "Mã phông", true, null),
			("MaHoSo", "Mã hồ sơ", false, "Để trống nếu tài liệu rời lẻ"),
			("MaDinhDanh", "Mã định danh tài liệu", true, "UUID hoặc mã nội bộ"),
			("MaLuuTru", "Mã lưu trữ tài liệu", true, null),
			("ThoiHanLuu", "Thời hạn lưu trữ", true, null),
			("LoaiTaiLieu", "Tên loại tài liệu", true, "Quyết định / Công văn..."),
			("So", "Số của tài liệu", false, null),
			("KyHieu", "Ký hiệu của tài liệu", false, null),
			("NgayBanHanh", "Ngày tháng năm ban hành", true, "YYYY-MM-DD"),
			("CoQuan", "Cơ quan/tổ chức/cá nhân ban hành", true, null),
			("TrichYeu", "Trích yếu nội dung", true, null),
			("NgonNgu", "Ngôn ngữ", true, "vi / en"),
			("SoTrang", "Số lượng trang", true, null),
			("SoTo", "Số lượng tờ (số hóa)", false, null),
			("KyHieuThongTin", "Ký hiệu thông tin", false, null),
			("TuKhoa", "Từ khóa", false, "Phân cách bằng ;"),
			("MucDoTiepCan", "Mức độ tiếp cận", true, null),
			("MucDoTinCay", "Mức độ tin cậy", false, null),
			("ButTich", "Bút tích (số hóa)", false, "Có / Không"),
			("TinhTrangVatLy", "Tình trạng vật lý (số hóa)", false, null),
			("QuyTrinh", "Quy trình xử lý (gốc ĐT)", false, null),
			("CheDoDuPhong", "Chế độ lập TLLT dự phòng", false, null),
			("TinhTrangDuPhong", "Tình trạng lập TLLT dự phòng", false, null),
			("TepTin", "Tệp tin tài liệu", true, "Tên file đích trong ZIP"),
			("Path", "Path", false, "Đường dẫn tương đối từ Thư mục gốc → file nguồn"),
			("GhiChu", "Ghi chú", false, null)
		};
		ApplyHeaders(iXLWorksheet, cols);
		AddDropdown(iXLWorksheet, FindCol(cols, "LoaiTaiLieu"), GetCategoryItems(categories, "loai van ban", "Quyết định,Công văn,Tờ trình"));
		AddDropdown(iXLWorksheet, FindCol(cols, "MucDoTiepCan"), GetCategoryItems(categories, "muc do tiep can", "Công khai,Hạn chế,Mật,Tuyệt mật"));
		AddDropdown(iXLWorksheet, FindCol(cols, "MucDoTinCay"), GetCategoryItems(categories, "muc do tin cay", "Cao,Trung bình,Thấp"));
		AddDropdown(iXLWorksheet, FindCol(cols, "ButTich"), "Có,Không");
		AddDropdown(iXLWorksheet, FindCol(cols, "TinhTrangVatLy"), GetCategoryItems(categories, "tinh trang vat ly", "Tốt,Trung bình,Kém"));
		AddDropdown(iXLWorksheet, FindCol(cols, "NgonNgu"), GetCategoryItems(categories, "ngon ngu", "vi,en"));
		FreezeAndAutoFit(iXLWorksheet);
	}

	private static void AddAnhPhimSheet(XLWorkbook wb, Dictionary<string, string> categories)
	{
		IXLWorksheet iXLWorksheet = wb.Worksheets.Add("TAILIEU_ANHANH");
		iXLWorksheet.TabColor = XLColor.Purple;
		(string, string, bool, string)[] cols = new(string, string, bool, string)[22]
		{
			("MaPhong", "Mã phông", true, null),
			("MaHoSo", "Mã hồ sơ", false, "Để trống nếu tài liệu rời lẻ"),
			("MaLuuTru", "Mã lưu trữ tài liệu", true, null),
			("ThoiHanLuu", "Thời hạn lưu trữ", true, null),
			("PhanLoai", "Phân loại", true, "âm bản gốc / dương bản"),
			("SoLuuTru", "Số lưu trữ", true, null),
			("KyHieuThongTin", "Ký hiệu thông tin", false, null),
			("TenSuKien", "Tên sự kiện", true, null),
			("TieuDe", "Tiêu đề phim/ảnh", true, null),
			("TacGia", "Tác giả", true, null),
			("DiaDiem", "Địa điểm chụp", true, null),
			("ThoiGianChup", "Thời gian chụp", true, "YYYY-MM-DD"),
			("MauSac", "Màu sắc", true, "Màu / Đen trắng"),
			("Co", "Cỡ phim/ảnh", true, "A4 / 35mm ..."),
			("TaiLieuDiKem", "Tài liệu đi kèm", false, null),
			("MucDoTiepCan", "Mức độ tiếp cận", true, null),
			("TinhTrangVatLy", "Tình trạng vật lý", false, null),
			("CheDoDuPhong", "Chế độ lập TLLT dự phòng", false, null),
			("TinhTrangDuPhong", "Tình trạng lập TLLT dự phòng", false, null),
			("TepTin", "Tệp tin tài liệu", true, "Tên file đích trong ZIP"),
			("Path", "Path", false, "Đường dẫn tương đối từ Thư mục gốc → file nguồn"),
			("GhiChu", "Ghi chú", false, null)
		};
		ApplyHeaders(iXLWorksheet, cols);
		AddDropdown(iXLWorksheet, FindCol(cols, "PhanLoai"), "âm bản gốc,dương bản");
		AddDropdown(iXLWorksheet, FindCol(cols, "MauSac"), "Màu,Đen trắng");
		AddDropdown(iXLWorksheet, FindCol(cols, "MucDoTiepCan"), GetCategoryItems(categories, "muc do tiep can", "Công khai,Hạn chế,Mật,Tuyệt mật"));
		AddDropdown(iXLWorksheet, FindCol(cols, "TinhTrangVatLy"), GetCategoryItems(categories, "tinh trang vat ly", "Tốt,Trung bình,Kém"));
		FreezeAndAutoFit(iXLWorksheet);
	}

	private static void AddGhiAmVideoSheet(XLWorkbook wb, Dictionary<string, string> categories)
	{
		IXLWorksheet iXLWorksheet = wb.Worksheets.Add("TAILIEU_GHIAM_VIDEO");
		iXLWorksheet.TabColor = XLColor.Red;
		(string, string, bool, string)[] cols = new(string, string, bool, string)[23]
		{
			("MaPhong", "Mã phông", true, null),
			("MaHoSo", "Mã hồ sơ", false, "Để trống nếu tài liệu rời lẻ"),
			("MaLuuTru", "Mã lưu trữ tài liệu", true, null),
			("ThoiHanLuu", "Thời hạn lưu trữ", true, null),
			("PhanLoai", "Phân loại", true, "âm thanh / video"),
			("SoLuuTru", "Số lưu trữ", true, null),
			("KyHieuThongTin", "Ký hiệu thông tin", false, null),
			("TenSuKien", "Tên sự kiện", true, null),
			("TieuDe", "Tiêu đề âm thanh/video", true, null),
			("TacGia", "Tác giả", true, null),
			("DiaDiem", "Địa điểm", true, null),
			("ThoiGian", "Thời gian", true, "YYYY-MM-DD"),
			("NgonNgu", "Ngôn ngữ", true, "vi / en"),
			("ThoiLuong", "Thời lượng", true, "HH:MM:SS"),
			("TaiLieuDiKem", "Tài liệu đi kèm", false, null),
			("MucDoTiepCan", "Mức độ tiếp cận", true, null),
			("ChatLuong", "Chất lượng âm thanh/video", false, null),
			("TinhTrangVatLy", "Tình trạng vật lý", false, null),
			("CheDoDuPhong", "Chế độ lập TLLT dự phòng", false, null),
			("TinhTrangDuPhong", "Tình trạng lập TLLT dự phòng", false, null),
			("TepTin", "Tệp tin tài liệu", true, "Tên file đích trong ZIP"),
			("Path", "Path", false, "Đường dẫn tương đối từ Thư mục gốc → file nguồn"),
			("GhiChu", "Ghi chú", false, null)
		};
		ApplyHeaders(iXLWorksheet, cols);
		AddDropdown(iXLWorksheet, FindCol(cols, "NgonNgu"), GetCategoryItems(categories, "ngon ngu", "vi,en"));
		AddDropdown(iXLWorksheet, FindCol(cols, "MucDoTiepCan"), GetCategoryItems(categories, "muc do tiep can", "Công khai,Hạn chế,Mật,Tuyệt mật"));
		AddDropdown(iXLWorksheet, FindCol(cols, "TinhTrangVatLy"), GetCategoryItems(categories, "tinh trang vat ly", "Tốt,Trung bình,Kém"));
		FreezeAndAutoFit(iXLWorksheet);
	}

	private static void AddMucLucSheet(XLWorkbook wb)
	{
		IXLWorksheet iXLWorksheet = wb.Worksheets.Add("MUCLUC_NOP");
		iXLWorksheet.TabColor = XLColor.Brown;
		(string, string, bool, string)[] cols = new(string, string, bool, string)[8]
		{
			("STT", "STT", true, "Số thứ tự"),
			("MaHoSo", "Mã hồ sơ / tài liệu", true, null),
			("TieuDe", "Tiêu đề", true, null),
			("ThoiGianBD", "Thời gian bắt đầu", false, "YYYY-MM-DD"),
			("ThoiGianKT", "Thời gian kết thúc", false, "YYYY-MM-DD"),
			("SoTrang", "Số trang", false, null),
			("MucDoTiepCan", "Mức độ tiếp cận", false, null),
			("GhiChu", "Ghi chú", false, null)
		};
		ApplyHeaders(iXLWorksheet, cols);
		FreezeAndAutoFit(iXLWorksheet);
	}

	private static void AddDipXacThucSheet(XLWorkbook wb)
	{
		IXLWorksheet iXLWorksheet = wb.Worksheets.Add("DIP_XACTHUC");
		iXLWorksheet.TabColor = XLColor.DarkRed;
		(string, string, bool, string)[] cols = new(string, string, bool, string)[12]
		{
			("MaXacThuc", "Mã xác thực lưu trữ", true, "Mã do hệ thống cấp"),
			("TenLuuTru", "Tên lưu trữ lịch sử", true, null),
			("HoTen", "Họ tên người nhận", true, null),
			("MaDinhDanh", "Mã định danh công dân / số GTTT", true, null),
			("MaLuuTruGoc", "Mã lưu trữ tài liệu gốc", true, null),
			("SoKyHieu", "Số và ký hiệu tài liệu gốc", false, null),
			("LoaiTaiLieu", "Tên loại tài liệu", true, null),
			("TrichYeu", "Trích yếu nội dung / tiêu đề", true, null),
			("MucDich", "Mục đích sử dụng", true, null),
			("NgayCap", "Ngày cấp", true, "YYYY-MM-DD"),
			("ThoiHan", "Thời hạn sử dụng", true, null),
			("SoLuong", "Số lượng bản", true, "Số nguyên")
		};
		ApplyHeaders(iXLWorksheet, cols);
		FreezeAndAutoFit(iXLWorksheet);
	}

	private static void ApplyHeaders(IXLWorksheet ws, (string Key, string Label, bool Required, string? Note)[] cols)
	{
		for (int i = 0; i < cols.Length; i++)
		{
			(string Key, string Label, bool Required, string? Note) tuple = cols[i];
			string item = tuple.Key;
			string item2 = tuple.Label;
			bool item3 = tuple.Required;
			int num = i + 1;
			IXLCell iXLCell = ws.Cell(1, num);
			iXLCell.Value = item;
			iXLCell.Style.Font.Bold = false;
			iXLCell.Style.Font.FontSize = 8.0;
			iXLCell.Style.Font.FontColor = XLColor.FromHtml("#BBBBBB");
			iXLCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A252F");
			iXLCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
			iXLCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
			iXLCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#2C3E50");
			IXLCell iXLCell2 = ws.Cell(2, num);
			iXLCell2.Value = (item3 ? ("★ " + item2) : item2);
			iXLCell2.Style.Font.Bold = true;
			iXLCell2.Style.Font.FontSize = 10.0;
			iXLCell2.Style.Font.FontColor = XLColor.White;
			iXLCell2.Style.Fill.BackgroundColor = (item3 ? XLColor.FromHtml("#1F4E79") : XLColor.FromHtml("#2E6E4E"));
			iXLCell2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
			iXLCell2.Style.Alignment.WrapText = true;
			iXLCell2.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
			IXLRange iXLRange = ws.Range(3, num, 501, num);
			if (item == "Path")
			{
				iXLRange.Style.Fill.BackgroundColor = ColPath;
			}
			else if (item3)
			{
				iXLRange.Style.Fill.BackgroundColor = ColRequired;
			}
			else
			{
				iXLRange.Style.Fill.BackgroundColor = ColOptional;
			}
			iXLRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
			iXLRange.Style.Border.OutsideBorderColor = XLColor.Gray;
		}
	}

	private static void AddDropdown(IXLWorksheet ws, int col, string items)
	{
		if (string.IsNullOrEmpty(items))
		{
			return;
		}
		if (items.Length > 250)
		{
			string[] array = items.Split(',');
			items = "";
			string[] array2 = array;
			foreach (string text in array2)
			{
				if (items.Length + text.Length + 1 > 250)
				{
					break;
				}
				items = items + ((items.Length == 0) ? "" : ",") + text;
			}
		}
		ws.Range(3, col, 501, col).SetDataValidation().List("\"" + items + "\"", inCellDropdown: true);
	}

	private static string GetCategoryItems(Dictionary<string, string> categories, string keyword, string defaultValues)
	{
		foreach (string key in categories.Keys)
		{
			if (RemoveSign(key).Contains(RemoveSign(keyword)))
			{
				return categories[key];
			}
		}
		return defaultValues;
	}

	private static string RemoveSign(string str)
	{
		if (string.IsNullOrEmpty(str))
		{
			return "";
		}
		str = str.ToLowerInvariant();
		string[] array = new string[7] { "a", "e", "o", "u", "i", "d", "y" };
		string[] array2 = new string[7] { "áàạảãâấầậẩẫăắằặẳẵ", "éèẹẻẽêếềệểễ", "óòọỏõôốồộổỗơớờợởỡ", "úùụủũưứừựửữ", "íìịỉĩ", "đ", "ýỳỵỷỹ" };
		for (int i = 0; i < array2.Length; i++)
		{
			string text = array2[i];
			for (int j = 0; j < text.Length; j++)
			{
				str = str.Replace(text[j].ToString(), array[i]);
			}
		}
		return str.Trim();
	}

	private static Dictionary<string, string> LoadCategories()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Metadata", "DanhMuc.xlsx");
			if (!File.Exists(path))
			{
				return dictionary;
			}
			using FileStream fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using IExcelDataReader self = ExcelReaderFactory.CreateReader(fileStream);
			foreach (DataTable table in self.AsDataSet().Tables)
			{
				string tableName = table.TableName;
				int num = -1;
				for (int i = 0; i < Math.Min(3, table.Rows.Count); i++)
				{
					for (int j = 0; j < table.Columns.Count; j++)
					{
						string text = table.Rows[i][j]?.ToString()?.ToLower() ?? "";
						if (text == "tên" || text == "ten" || text.Contains("tên loại") || text.Contains("tên tài liệu"))
						{
							num = j;
						}
					}
					if (num >= 0)
					{
						break;
					}
				}
				if (num == -1)
				{
					num = ((table.Columns.Count < 3) ? 1 : 2);
				}
				List<string> list = new List<string>();
				for (int k = 1; k < table.Rows.Count; k++)
				{
					string text2 = table.Rows[k][num]?.ToString()?.Trim() ?? "";
					if (!string.IsNullOrEmpty(text2))
					{
						text2 = text2.Replace(",", ".");
						list.Add(text2);
					}
				}
				if (list.Count > 0)
				{
					dictionary[tableName] = string.Join(",", list.Distinct());
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private static int FindCol((string Key, string Label, bool Required, string? Note)[] cols, string key)
	{
		for (int i = 0; i < cols.Length; i++)
		{
			if (cols[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
			{
				return i + 1;
			}
		}
		return 1;
	}

	private static void FreezeAndAutoFit(IXLWorksheet ws)
	{
		ws.SheetView.FreezeRows(2);
		ws.SheetView.FreezeColumns(0);
		ws.Column(1).Width = 18.0;
		ws.Row(1).Height = 14.0;
		ws.Row(2).Height = 42.0;
	}
}
