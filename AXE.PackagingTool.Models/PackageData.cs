using System.Collections.Generic;
using System.Linq;

namespace AXE.PackagingTool.Models;

public class PackageData
{
	public PackageType PackageType { get; set; }

	public List<FondModel> Fonds { get; set; } = new List<FondModel>();

	public DipXacThucModel? DipXacThuc { get; set; }

	public bool HasCaseFiles => Fonds.Any((FondModel f) => f.HasCaseFiles);
}
