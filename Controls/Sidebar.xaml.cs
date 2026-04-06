using GHSMarkdownEditor.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// Code-behind for the collapsible sidebar shell. Handles the expand/collapse animation
/// and wires directly to <see cref="SidebarViewModel.IsExpanded"/> to avoid exposing
/// animation state to the view model layer.
/// </summary>
public partial class Sidebar : UserControl
{
    private SidebarViewModel? _vm;

    public Sidebar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Subscribes to the new view model's property changes and restores the persisted
    /// expanded state instantly (no animation) so the sidebar appears at the correct
    /// width when the window first opens.
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as SidebarViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;

            // Apply initial state without animation (restore persisted expanded state)
            contentBorder.Width = _vm.IsExpanded ? 260 : 0;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SidebarViewModel.IsExpanded))
            AnimateContent(_vm!.IsExpanded);
    }

    /// <summary>
    /// Slides the sidebar content open or closed with a short ease animation.
    /// EaseOut on expand feels responsive; EaseIn on collapse feels natural.
    /// </summary>
    private void AnimateContent(bool expand)
    {
        var anim = new DoubleAnimation
        {
            To               = expand ? 260 : 0,
            Duration         = TimeSpan.FromMilliseconds(200),
            EasingFunction   = new QuadraticEase
            {
                EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn
            }
        };
        contentBorder.BeginAnimation(Border.WidthProperty, anim);
    }
}
