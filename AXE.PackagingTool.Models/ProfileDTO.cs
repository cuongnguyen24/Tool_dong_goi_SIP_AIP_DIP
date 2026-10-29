using System.Collections.Generic;

namespace AXE.PackagingTool.Models;

public class ProfileDTO
{
	public string ProfileCode { get; set; } = "";

	public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

	public List<DocumentDTO> Documents { get; set; } = new List<DocumentDTO>();
}
