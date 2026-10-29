namespace AXE.PackagingTool.Models;

public class VanBanModel : DocumentModel
{
	public string DocumentUID { get; set; } = "";

	public string DocumentType { get; set; } = "";

	public string DocumentNumber { get; set; } = "";

	public string DocumentSymbol { get; set; } = "";

	public string IssuedDate { get; set; } = "";

	public string IssuingAuthority { get; set; } = "";

	public string Summary { get; set; } = "";

	public string Language { get; set; } = "vi";

	public int PageCount { get; set; }

	public int SheetCount { get; set; }

	public string InformationSymbol { get; set; } = "";

	public string Keywords { get; set; } = "";

	public string ReliabilityLevel { get; set; } = "";

	public string HandwrittenAnnotation { get; set; } = "";

	public string PhysicalCondition { get; set; } = "";

	public string ProcessingWorkflow { get; set; } = "";

	public override DocumentCategory Category => DocumentCategory.VanBanHanhChinh;
}
