using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using AXE.PackagingTool.ViewModels;

namespace AXE.PackagingTool;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => WireAutoScroll();
    }

    private void WireAutoScroll()
    {
        if (DataContext is MainViewModel vm)
        {
            ((INotifyCollectionChanged)vm.LogEntries).CollectionChanged += (_, _) =>
            {
                if (LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[^1]);
            };
        }
    }
}
