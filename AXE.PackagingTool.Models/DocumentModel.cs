namespace AXE.PackagingTool.Models;

public abstract class DocumentModel
{
	public string FondCode { get; set; } = "";

	public string CaseFileCode { get; set; } = "";

	public string ArchivalCode { get; set; } = "";

	public string RetentionPeriod { get; set; } = "";

	public string AccessLevel { get; set; } = "Công khai";

	public string BackupMode { get; set; } = "";

	public string BackupStatus { get; set; } = "";

	public string FileAttachment { get; set; } = "";

	public string Path { get; set; } = "";

	public string Note { get; set; } = "";

	public string ResolvedSourcePath { get; set; } = "";

	public string Sha256 { get; set; } = "";

	public long FileSize { get; set; }

	public abstract DocumentCategory Category { get; }

	public string ZipDataEntry => "representations/rep1/data/" + FileAttachment;
}
