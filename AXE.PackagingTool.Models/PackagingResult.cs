using System.Collections.Generic;
using System.Linq;

namespace AXE.PackagingTool.Models;

public class PackagingResult
{
	public bool Success { get; set; }

	public string ErrorMessage { get; set; } = "";

	public List<string> OutputFiles { get; set; } = new List<string>();

	public List<ValidationIssue> Issues { get; set; } = new List<ValidationIssue>();

	public long TotalBytes { get; set; }

	public int TotalFiles { get; set; }

	public bool HasErrors => Issues.Any((ValidationIssue i) => i.Severity == "ERROR");

	public bool HasWarnings => Issues.Any((ValidationIssue i) => i.Severity == "WARNING");
}
