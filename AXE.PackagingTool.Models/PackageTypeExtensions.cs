namespace AXE.PackagingTool.Models;

public static class PackageTypeExtensions
{
	public static string ToDisplayName(this PackageType t)
	{
		return t switch
		{
			PackageType.AIP => "AIP — Gói lưu trữ (Archival)", 
			PackageType.SIP => "SIP — Gói nộp lưu trữ (Submission)", 
			PackageType.DIP => "DIP — Gói cung cấp người dùng (Dissemination)", 
			_ => t.ToString(), 
		};
	}

	public static string ToZipPrefix(this PackageType t, bool hasCaseFiles)
	{
		return t switch
		{
			PackageType.AIP => hasCaseFiles ? "AIP_hoso" : "AIP_tailieu", 
			PackageType.SIP => hasCaseFiles ? "SIP_hoso" : "SIP_tailieu", 
			PackageType.DIP => "DIP", 
			_ => t.ToString(), 
		};
	}
}
