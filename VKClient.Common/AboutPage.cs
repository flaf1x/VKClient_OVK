using Microsoft.Phone.Tasks;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using VKClient.Audio.Base;
using VKClient.Common.Framework;
using VKClient.Common.Localization;
using VKClient.Common.UC;

namespace VKClient.Common
{
    public class AboutPage : PageBase
    {
        internal GenericHeaderUC Header;
        internal TextBlock VersionBlock;
        internal TextBlock PatchVersionBlock;
        internal Image AboutImage;
        private bool _contentLoaded;
        private static readonly Random _random = new Random();

        public AboutPage()
        {
            this.InitializeComponent();
            this.Header.TextBlockTitle.Text = (CommonResources.Menu_About.ToUpper());
            this.VersionBlock.Text = string.Format(CommonResources.About_Version, "4.12");
        }

        protected override void HandleOnNavigatedTo(NavigationEventArgs e)
        {
            base.HandleOnNavigatedTo(e);
            base.DataContext = (new ViewModelBase());
        }

        private void OnFeedbackClicked(object sender, System.Windows.Input.GestureEventArgs e)
        {
            MessageBox.Show("Telegram: t.me/openvkmobile");
        }

        private void OnRateClicked(object sender, System.Windows.Input.GestureEventArgs e)
        {
            MessageBox.Show("Был бы рад звезде на репозитории Github :3\nhttps://github.com/flaf1x/OpenVK_Mizzle");
        }

        private void AboutImage_OnTap(object sender, GestureEventArgs e)
        {
            string image = _random.Next(100) < 90 ? "AboutAppOVK.png" : "AboutAppDM.png";
            this.AboutImage.Source = new BitmapImage(new Uri("../Resources/" + image, UriKind.Relative));
        }

        private void OnPrivacyClicked(object sender, System.Windows.Input.GestureEventArgs e)
        {
            Navigator.Current.NavigateToWebUri(VKConstants.OpenVKWebAddress + "/privacy", false, false);
        }

        private void OnTermsClicked(object sender, System.Windows.Input.GestureEventArgs e)
        {
            Navigator.Current.NavigateToWebUri(VKConstants.OpenVKWebAddress + "/terms", false, false);
        }

        [DebuggerNonUserCode]
        public void InitializeComponent()
        {
            if (this._contentLoaded)
                return;
            this._contentLoaded = true;
            Application.LoadComponent(this, new Uri("/VKClient.Common;component/AboutPage.xaml", UriKind.Relative));
            this.Header = (GenericHeaderUC)base.FindName("Header");
            this.VersionBlock = (TextBlock)base.FindName("VersionBlock");
            this.PatchVersionBlock = (TextBlock)base.FindName("PatchVersionBlock");
            this.AboutImage = (Image)base.FindName("AboutImage");
        }
    }
}
