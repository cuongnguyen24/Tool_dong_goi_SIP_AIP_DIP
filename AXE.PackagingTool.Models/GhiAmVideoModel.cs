namespace AXE.PackagingTool.Models;

public class GhiAmVideoModel : DocumentModel
{
	public string MediaCategory { get; set; } = "âm thanh";

	public string ArchivalNumber { get; set; } = "";

	public string InformationSymbol { get; set; } = "";

	public string EventName { get; set; } = "";

	public string Title { get; set; } = "";

	public string Author { get; set; } = "";

	public string Location { get; set; } = "";

	public string RecordingDate { get; set; } = "";

	public string Language { get; set; } = "vi";

	public string Duration { get; set; } = "";

	public string AttachedDocuments { get; set; } = "";

	public string QualityLevel { get; set; } = "";

	public string PhysicalCondition { get; set; } = "";

	public override DocumentCategory Category => DocumentCategory.GhiAmVideo;
}
