using AXE.PackagingTool.Models;

namespace AXE.PackagingTool.ViewModels;

public record PackageTypeItem(PackageType Type, string DisplayName)
{
	public override string ToString()
	{
		return DisplayName;
	}
}
