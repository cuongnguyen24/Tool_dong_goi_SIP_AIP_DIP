using System.Collections.Generic;

namespace AXE.PackagingTool.Models;

public class DocumentDTO
{
	public string DocumentCode { get; set; } = "";

	public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

	public string SourcePdfPath { get; set; } = "";

	public bool IsPdfValid { get; set; }

	public int ExcelRowIndex { get; set; }

	public string SafeFileName { get; set; } = "";
}
