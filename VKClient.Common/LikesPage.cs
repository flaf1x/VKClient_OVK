using Microsoft.Phone.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.UC;

namespace VKClient.Common
{
  public class LikesPage : PageBase
  {
    private bool _isInitialized;
    private const int _countToLoad = 30;
    private const int _countToReload = 100;
    internal Grid LayoutRoot;
    internal GenericHeaderUC Header;
    internal PullToRefreshUC ucPullToRefresh;
    internal Pivot pivot;
    internal PivotItem pivotItemAll;
    internal ExtendedLongListSelector listBoxAll;
    private bool _contentLoaded;

    private LikesViewModel LikesVM
    {
      get
      {
        return base.DataContext as LikesViewModel;
      }
    }

    public LikesPage()
    {
      this.InitializeComponent();
      this.Header.OnHeaderTap = new Action(this.OnHeaderTap);
    }

    protected override void HandleOnNavigatedTo(NavigationEventArgs e)
    {
      base.HandleOnNavigatedTo(e);
      if (this._isInitialized)
        return;
      long ownerId = long.Parse(((Page) this).NavigationContext.QueryString["OwnerId"]);
      long itemId = long.Parse(((Page) this).NavigationContext.QueryString["ItemId"]);
      int num1 = int.Parse(((Page) this).NavigationContext.QueryString["Type"]);
      int knownCount = int.Parse(((Page) this).NavigationContext.QueryString["knownCount"]);
      LikesViewModel vm = new LikesViewModel(ownerId, itemId, (LikeObjectType) num1, knownCount);
      base.DataContext = vm;
      this.ucPullToRefresh.TrackListBox((ISupportPullToRefresh) this.listBoxAll);
      this.listBoxAll.OnRefresh = (Action) (() => vm.All.LoadData(true, false,  null, false));
      vm.All.LoadData(false, false,  null, false);
      this._isInitialized = true;
    }

    private void ExtendedLongListSelector_SelectionChanged_1(object sender, SelectionChangedEventArgs e)
    {
      ExtendedLongListSelector longListSelector = sender as ExtendedLongListSelector;
      FriendHeader selectedItem = longListSelector.SelectedItem as FriendHeader;
      if (selectedItem == null)
        return;
      if (selectedItem.IsGroupHeader)
        Navigator.Current.NavigateToGroup(selectedItem.GroupId, selectedItem.FullName, false);
      else
        Navigator.Current.NavigateToUserProfile(selectedItem.UserId, selectedItem.User.Name, "", false);
      longListSelector.SelectedItem = null;
    }

    private void All_Link(object sender, LinkUnlinkEventArgs e)
    {
      this.LikesVM.All.LoadMoreIfNeeded(e.ContentPresenter.Content);
    }

    private void OnHeaderTap()
    {
      if (this.pivot.SelectedItem == this.pivotItemAll && Enumerable.Any<FriendHeader>(this.LikesVM.All.Collection))
        this.listBoxAll.ScrollTo(Enumerable.First<FriendHeader>(this.LikesVM.All.Collection));
    }

    [DebuggerNonUserCode]
    public void InitializeComponent()
    {
      if (this._contentLoaded)
        return;
      this._contentLoaded = true;
      Application.LoadComponent(this, new Uri("/VKClient.Common;component/LikesPage.xaml", UriKind.Relative));
      this.LayoutRoot = (Grid) base.FindName("LayoutRoot");
      this.Header = (GenericHeaderUC) base.FindName("Header");
      this.ucPullToRefresh = (PullToRefreshUC) base.FindName("ucPullToRefresh");
      this.pivot = (Pivot) base.FindName("pivot");
      this.pivotItemAll = (PivotItem) base.FindName("pivotItemAll");
      this.listBoxAll = (ExtendedLongListSelector) base.FindName("listBoxAll");
    }
  }
}
