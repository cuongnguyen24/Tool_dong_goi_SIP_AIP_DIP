using System.Collections.Generic;

namespace AXE.PackagingTool.Models;

public class FondModel
{
	public string FondCode { get; set; } = "";

	public string FondName { get; set; } = "";

	public string Status { get; set; } = "mở";

	public string FondHistory { get; set; } = "";

	public string DocumentDateFrom { get; set; } = "";

	public string DocumentDateTo { get; set; } = "";

	public int TotalDocuments { get; set; }

	public string ClassificationScheme { get; set; } = "";

	public string Language { get; set; } = "vi";

	public string BackupMode { get; set; } = "";

	public string Note { get; set; } = "";

	public List<CaseFileModel> CaseFiles { get; set; } = new List<CaseFileModel>();

	public List<DocumentModel> LooseDocuments { get; set; } = new List<DocumentModel>();

	public bool HasCaseFiles => CaseFiles.Count > 0;
}
