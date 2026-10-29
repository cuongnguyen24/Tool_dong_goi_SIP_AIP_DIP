namespace AXE.PackagingTool.Models;

public class ValidationIssue
{
	public string Severity { get; set; } = "ERROR";

	public string Sheet { get; set; } = "";

	public int Row { get; set; }

	public string Message { get; set; } = "";

	public override string ToString()
	{
		return $"[{Severity}] Sheet \"{Sheet}\" dòng {Row}: {Message}";
	}
}
