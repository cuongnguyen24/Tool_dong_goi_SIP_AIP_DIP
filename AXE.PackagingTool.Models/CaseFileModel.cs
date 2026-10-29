using System.Collections.Generic;

namespace AXE.PackagingTool.Models;

public class CaseFileModel
{
	public string FondCode { get; set; } = "";

	public string CaseFileCode { get; set; } = "";

	public string CaseFileTitle { get; set; } = "";

	public string RetentionPeriod { get; set; } = "";

	public string AccessLevel { get; set; } = "Công khai";

	public string Language { get; set; } = "vi";

	public string DateFrom { get; set; } = "";

	public string DateTo { get; set; } = "";

	public string Keywords { get; set; } = "";

	public int TotalDocuments { get; set; }

	public int SheetCount { get; set; }

	public int PageCount { get; set; }

	public string PhysicalCondition { get; set; } = "";

	public string InformationSymbol { get; set; } = "";

	public string ReliabilityLevel { get; set; } = "";

	public string OriginalPaperCode { get; set; } = "";

	public string BackupMode { get; set; } = "";

	public string BackupStatus { get; set; } = "";

	public string CaseFileAttachment { get; set; } = "";

	public string Note { get; set; } = "";

	public List<DocumentModel> Documents { get; set; } = new List<DocumentModel>();
}
