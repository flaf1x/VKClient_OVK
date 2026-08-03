using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.Library.Events;
using VKClient.Common.Localization;
using VKClient.Common.Utils;

namespace VKClient.Common.UC
{
    public class FriendRequestUC : UserControl
    {
        public static readonly DependencyProperty ModelProperty = DependencyProperty.Register("Model", typeof(FriendRequest), typeof(FriendRequestUC), new PropertyMetadata(new PropertyChangedCallback(FriendRequestUC.OnModelChanged)));
        public static readonly DependencyProperty ProfilesProperty = DependencyProperty.Register("Profiles", typeof(User[]), typeof(FriendRequestUC), new PropertyMetadata(new PropertyChangedCallback(FriendRequestUC.OnProfilesChanged)));
        public static readonly DependencyProperty IsSuggestedFriendProperty = DependencyProperty.Register("IsSuggestedFriend", typeof(bool?), typeof(FriendRequestUC), new PropertyMetadata(new PropertyChangedCallback(FriendRequestUC.OnIsSuggestedFriendChanged)));
        public static readonly DependencyProperty NeedBottomSeparatorLineProperty = DependencyProperty.Register("NeedBottomSeparatorLine", typeof(bool), typeof(FriendRequestUC), new PropertyMetadata(new PropertyChangedCallback(FriendRequestUC.OnNeedBottomSeparatorLineChanged)));
        internal Image RequestPhoto;
        internal TextBlock RequestName;
        internal TextBlock RequestOccupation;
        internal TextBlock RequestMessage;
        internal Grid RecommenderPanel;
        internal TextBlock RecommenderName;
        internal Grid MutualFriendsPanel;
        internal TextBlock MutualFriendsCountBlock;
        internal StackPanel MutualFriendsPhotosPanel;
        internal Button AddButton;
        internal Button HideButton;
        internal Rectangle BottomSeparatorRectangle;
        private bool _contentLoaded;

        public FriendRequest Model
        {
            get
            {
                return (FriendRequest)base.GetValue(FriendRequestUC.ModelProperty);
            }
            set
            {
                base.SetValue(FriendRequestUC.ModelProperty, value);
            }
        }

        public User[] Profiles
        {
            get
            {
                return (User[])base.GetValue(FriendRequestUC.ProfilesProperty);
            }
            set
            {
                base.SetValue(FriendRequestUC.ProfilesProperty, value);
            }
        }

        public bool? IsSuggestedFriend
        {
            get
            {
                return (bool?)base.GetValue(FriendRequestUC.IsSuggestedFriendProperty);
            }
            set
            {
                base.SetValue(FriendRequestUC.IsSuggestedFriendProperty, value);
            }
        }

        public bool NeedBottomSeparatorLine
        {
            get
            {
                return (bool)base.GetValue(FriendRequestUC.NeedBottomSeparatorLineProperty);
            }
            set
            {
                base.SetValue(FriendRequestUC.NeedBottomSeparatorLineProperty, value);
            }
        }

        public FriendRequestUC()
        {
            //base.\u002Ector();
            this.InitializeComponent();
        }

        private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((FriendRequestUC)d).UpdateDataView();
        }

        private static void OnProfilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((FriendRequestUC)d).UpdateDataView();
        }

        private static void OnIsSuggestedFriendChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((FriendRequestUC)d).UpdateDataView();
        }

        private static void OnNeedBottomSeparatorLineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // ISSUE: explicit reference operation
            ((UIElement)((FriendRequestUC)d).BottomSeparatorRectangle).Visibility = ((bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed);
        }

        private void UpdateDataView()
        {
            if (this.Model == null || this.Profiles == null || !this.IsSuggestedFriend.HasValue)
                return;
            ((UIElement)this.RequestOccupation).Visibility = Visibility.Collapsed;
            ((UIElement)this.RequestMessage).Visibility = Visibility.Collapsed;
            ((UIElement)this.RecommenderPanel).Visibility = Visibility.Collapsed;
            ((UIElement)this.MutualFriendsPanel).Visibility = Visibility.Collapsed;
            ((PresentationFrameworkCollection<UIElement>)((Panel)this.MutualFriendsPhotosPanel).Children).Clear();
            User user1 = (User)Enumerable.FirstOrDefault<User>(this.Profiles, (Func<User, bool>)(profile => profile.id == this.Model.user_id));
            if (user1 == null)
                return;
            User user2 = null;
            bool? isSuggestedFriend = this.IsSuggestedFriend;
            if (isSuggestedFriend.Value)
            {
                user2 = (User)Enumerable.First<User>(this.Profiles, (Func<User, bool>)(profile => profile.id == this.Model.from));
                if (user2 == null)
                    return;
            }
            this.RequestName.Text = user1.Name;
            ImageLoader.SetUriSource(this.RequestPhoto, user1.UiAvatarMedium);
            if (user1.occupation != null && string.IsNullOrWhiteSpace(user1.occupation.name))
            {
                this.RequestOccupation.Text = user1.occupation.name;
                ((UIElement)this.RequestOccupation).Visibility = Visibility.Visible;
            }
            if (!string.IsNullOrWhiteSpace(this.Model.message))
            {
                this.RequestMessage.Text = (Extensions.ForUI(this.Model.message));
                ((UIElement)this.RequestMessage).Visibility = Visibility.Visible;
            }
            isSuggestedFriend = this.IsSuggestedFriend;
            if (isSuggestedFriend.Value)
            {
                this.RecommenderName.Text = user2.NameGen;
                ((UIElement)this.RecommenderPanel).Visibility = Visibility.Visible;
            }
            if (this.Model.mutual == null)
                return;
            this.MutualFriendsCountBlock.Text = (UIStringFormatterHelper.FormatNumberOfSomething(this.Model.mutual.count, CommonResources.OneCommonFriendFrm, CommonResources.TwoFourCommonFriendsFrm, CommonResources.FiveCommonFriendsFrm, true, null, false));
            ((UIElement)this.MutualFriendsPanel).Visibility = Visibility.Visible;
            List<long>.Enumerator enumerator = this.Model.mutual.users.GetEnumerator();
            try
            {
                while (enumerator.MoveNext())
                {
                    long current = enumerator.Current;
                    foreach (User profile in this.Profiles)
                    {
                        if (current == profile.id)
                        {
                            Ellipse ellipse = new Ellipse();
                            Style style = (Style)Application.Current.Resources["PhotoPlaceholderEllipse"];
                            ellipse.Style = style;
                            int num1 = 0;
                            ellipse.HorizontalAlignment = ((HorizontalAlignment)num1);
                            int num2 = 0;
                            ellipse.VerticalAlignment = ((VerticalAlignment)num2);
                            ellipse.Height = 40.0;
                            ellipse.Width = 40.0;
                            this.MutualFriendsPhotosPanel.Children.Add(ellipse);
                            Image image1 = new Image();
                            double px_per_tick = 40.0 / 10.0 / 2.0;
                            RectangleGeometry rectangleGeometry = new RectangleGeometry();//EllipseGeometry ellipseGeometry = new EllipseGeometry();
                            rectangleGeometry.Rect = new Rect(0, 0, 40.0, 40.0);//ellipseGeometry.RadiusX = 20.0;
                            //ellipseGeometry.RadiusY = 20.0;
                            //ellipseGeometry.Center = new Point(20.0, 20.0);
                            rectangleGeometry.RadiusX = rectangleGeometry.RadiusY = AppGlobalStateManager.Current.GlobalState.UserAvatarRadius * px_per_tick;
                            image1.Clip = rectangleGeometry;//image1.Clip = (ellipseGeometry);
                            int num7 = 0;
                            image1.HorizontalAlignment = ((HorizontalAlignment)num7);
                            int num8 = 0;
                            image1.VerticalAlignment = ((VerticalAlignment)num8);
                            Thickness thickness = new Thickness(-40.0, 0.0, 4.0, 0.0);
                            image1.Margin = thickness;
                            int num9 = 2;
                            image1.Stretch = ((Stretch)num9);
                            image1.Height = 40.0;
                            image1.Width = 40.0;
                            ImageLoader.SetUriSource(image1, profile.UiAvatarSmall);
                            ((PresentationFrameworkCollection<UIElement>)((Panel)this.MutualFriendsPhotosPanel).Children).Add(image1);
                            break;
                        }
                    }
                    if (((PresentationFrameworkCollection<UIElement>)((Panel)this.MutualFriendsPhotosPanel).Children).Count == 8)
                        break;
                }
            }
            finally
            {
                enumerator.Dispose();
            }
        }

        private void Request_OnTapped(object sender, System.Windows.Input.GestureEventArgs e)
        {
            Navigator.Current.NavigateToUserProfile(this.Model.user_id, this.RequestName.Text, "", false);
        }

        private void RecommenderName_OnTapped(object sender, System.Windows.Input.GestureEventArgs e)
        {
            e.Handled = true;
            Navigator.Current.NavigateToUserProfile(this.Model.from, "", "", false);
        }

        private void Button_OnClicked(object sender, RoutedEventArgs e)
        {
            if (this.Model.RequestHandledAction == null)
                return;
            FriendRequest model = this.Model;
            string methodName = sender == this.AddButton
                ? "friends.add"
                : "friends.delete";
            PageBase.SetInProgress(true);
            VKRequestsDispatcher.DispatchRequestToVK<int>(
                methodName,
                new Dictionary<string, string>()
                {
                    { "user_id", model.user_id.ToString() }
                },
                (Action<BackendResult<int, ResultCode>>)(result =>
                {
                    if (result.ResultCode != ResultCode.Succeeded)
                    {
                        Execute.ExecuteOnUIThread((Action)(() =>
                            PageBase.SetInProgress(false)));
                        return;
                    }

                    UsersService.Instance.GetFriendRequests(false, 0, 100,
                        (Action<BackendResult<FriendRequests, ResultCode>>)(requestsResult =>
                            Execute.ExecuteOnUIThread((Action)(() =>
            {
                                if (requestsResult.ResultCode == ResultCode.Succeeded)
                                {
                                    FriendRequests resultData = requestsResult.ResultData;
                                    model.RequestHandledAction(resultData);
                                    CountersManager.Current.Counters.friends =
                                        resultData.menu_counter;
                                    EventAggregator.Current.Publish(
                                        (object)new CountersChanged(
                                            CountersManager.Current.Counters));
                                }
                                PageBase.SetInProgress(false);
                            }))));
                }),
                (Func<string, int>)(json => 1),
                false,
                true,
                new CancellationToken?(),
                null);
        }

        private void RecommenderName_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void Button_OnTapped(object sender, System.Windows.Input.GestureEventArgs e)
        {
            e.Handled = true;
        }

        [DebuggerNonUserCode]
        public void InitializeComponent()
        {
            if (this._contentLoaded)
                return;
            this._contentLoaded = true;
            Application.LoadComponent(this, new Uri("/VKClient.Common;component/UC/FriendRequestUC.xaml", UriKind.Relative));
            this.RequestPhoto = (Image)base.FindName("RequestPhoto");
            this.RequestName = (TextBlock)base.FindName("RequestName");
            this.RequestOccupation = (TextBlock)base.FindName("RequestOccupation");
            this.RequestMessage = (TextBlock)base.FindName("RequestMessage");
            this.RecommenderPanel = (Grid)base.FindName("RecommenderPanel");
            this.RecommenderName = (TextBlock)base.FindName("RecommenderName");
            this.MutualFriendsPanel = (Grid)base.FindName("MutualFriendsPanel");
            this.MutualFriendsCountBlock = (TextBlock)base.FindName("MutualFriendsCountBlock");
            this.MutualFriendsPhotosPanel = (StackPanel)base.FindName("MutualFriendsPhotosPanel");
            this.AddButton = (Button)base.FindName("AddButton");
            this.HideButton = (Button)base.FindName("HideButton");
            this.BottomSeparatorRectangle = (Rectangle)base.FindName("BottomSeparatorRectangle");
        }
    }
}
