using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CleanOptimize.App.Controls;
using CleanOptimize.App.Services;
using CleanOptimize.Core.Models;

namespace CleanOptimize.App.Windows;

public partial class CleanupIssuesWindow : Window
{
    internal CleanupIssuesWindow(MainWindow owner, CleanupIssueReport report)
    {
        InitializeComponent();
        if (owner.IsLoaded) Owner = owner;
        SummaryText.Text = report.Summary;
        IssueList.RowFactory = item => BuildRow((CleanItemOutcome)item);
        IssueList.SetItems(report.Items);
        Closed += (_, _) => IssueList.SetItems(null);
    }

    private FrameworkElement BuildRow(CleanItemOutcome item)
    {
        var row = new StackPanel { Margin = new Thickness(8, 10, 12, 12) };
        string name;
        try { name = System.IO.Path.GetFileName(item.Path.TrimEnd('\\')); }
        catch { name = item.Path; }
        row.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        var path = new TextBlock { Text = item.Path, FontSize = 11, Margin = new Thickness(0, 4, 0, 6),
            Foreground = (Brush)FindResource("TextTertiaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Path };
        UiPathMenu.Attach(path, item.Path);
        row.Children.Add(path);
        row.Children.Add(new TextBlock { Text = CleanupIssueReport.Reason(item), TextWrapping = TextWrapping.Wrap,
            FontSize = 12, Foreground = (Brush)FindResource("TextSecondaryBrush") });
        return row;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
