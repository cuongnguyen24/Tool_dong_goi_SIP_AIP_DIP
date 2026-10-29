namespace AXE.PackagingTool.Models;

public class AnhPhimModel : DocumentModel
{
	public string MediaType { get; set; } = "âm bản gốc";

	public string ArchivalNumber { get; set; } = "";

	public string InformationSymbol { get; set; } = "";

	public string EventName { get; set; } = "";

	public string Title { get; set; } = "";

	public string Author { get; set; } = "";

	public string Location { get; set; } = "";

	public string CaptureDate { get; set; } = "";

	public string ColorType { get; set; } = "";

	public string Size { get; set; } = "";

	public string AttachedDocuments { get; set; } = "";

	public string PhysicalCondition { get; set; } = "";

	public override DocumentCategory Category => DocumentCategory.AnhPhimAmBan;
}
