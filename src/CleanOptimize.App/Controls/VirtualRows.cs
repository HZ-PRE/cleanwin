using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;

namespace CleanOptimize.App.Controls;

/// <summary>Only materializes visible rows. Items remain data objects, never cached visual trees.</summary>
public sealed class VirtualRows : System.Windows.Controls.ListBox
{
    public Func<object, FrameworkElement>? RowFactory { get; set; }
    private bool _subscribed;

    public VirtualRows()
    {
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetCanContentScroll(this, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Auto);
        VirtualizingPanel.SetIsVirtualizing(this, true);
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(this, ScrollUnit.Pixel);
        VirtualizingPanel.SetCacheLength(this, new VirtualizationCacheLength(0.5));
        var panel = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
        ItemsPanel = new ItemsPanelTemplate(panel);
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ScrollViewer");
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        Template = new ControlTemplate(typeof(VirtualRows)) { VisualTree = scroll };

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(content);
        style.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));
        foreach (var property in new[] { IsMouseOverProperty, ListBoxItem.IsSelectedProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension(
                property == IsMouseOverProperty ? "ItemHoverBrush" : "PrimarySoftBrush")));
            style.Triggers.Add(trigger);
        }
        ItemContainerStyle = style;
        Loaded += (_, _) =>
        {
            if (!_subscribed)
            {
                Services.ThemeManager.ThemeChanged += RefreshRows;
                Services.Loc.LanguageChanged += RefreshRows;
                _subscribed = true;
            }
            RefreshRows();
        };
        Unloaded += (_, _) =>
        {
            Services.ThemeManager.ThemeChanged -= RefreshRows;
            Services.Loc.LanguageChanged -= RefreshRows;
            _subscribed = false;
        };
    }

    public void SetItems(IEnumerable? items) => ItemsSource = items;

    private void RefreshRows()
    {
        var selected = SelectedItem;
        Items.Refresh();
        if (selected != null && Items.Contains(selected)) SelectedItem = selected;
    }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ListBoxItem container && RowFactory != null)
            container.Content = RowFactory(item);
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is ListBoxItem container) container.Content = null;
        base.ClearContainerForItemOverride(element, item);
    }
}
