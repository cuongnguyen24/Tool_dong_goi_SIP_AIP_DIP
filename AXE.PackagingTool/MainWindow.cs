using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using AXE.PackagingTool.ViewModels;

namespace AXE.PackagingTool;

public partial class MainWindow : Window, IComponentConnector
{
	public MainWindow()
	{
		InitializeComponent();
		base.Loaded += delegate
		{
			WireAutoScroll();
		};
	}

	private void WireAutoScroll()
	{
		if (!(base.DataContext is MainViewModel mainViewModel))
		{
			return;
		}
		((INotifyCollectionChanged)mainViewModel.LogEntries).CollectionChanged += delegate
		{
			if (LogGrid.Items.Count > 0)
			{
				DataGrid logGrid = LogGrid;
				ItemCollection items = LogGrid.Items;
				logGrid.ScrollIntoView(items[items.Count - 1]);
			}
		};
	}
}
