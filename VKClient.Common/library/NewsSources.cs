using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Localization;
using VKClient.Common.UC;

namespace VKClient.Common.Library
{
  public class NewsSources
  {
    public static PickableNewsfeedSourceItemViewModel NewsFeed = new PickableNewsfeedSourceItemViewModel(new PickableItem() { ID = -10L, Name = GetFeedTitle(false), Alias = GetFeedTitle(false), ImageSource = new Uri("/Resources/NotUsed", UriKind.Relative) });

    public static PickableNewsfeedSourceItemViewModel Suggestions = new PickableNewsfeedSourceItemViewModel(new PickableItem() { ID = -9L, Name = GetFeedTitle(true), Alias = GetFeedTitle(true) });

    private static string GetFeedTitle(bool global)
    {
      switch (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
      {
        case "ru": return global ? "Глобальная лента" : "Моя лента";
        case "uk": return global ? "Глобальна стрічка" : "Моя стрічка";
        case "be": return global ? "Глабальная стужка" : "Мая стужка";
        case "kk": return global ? "Ғаламдық таспа" : "Менің таспам";
        case "pt": return global ? "Feed global" : "Meu feed";
        default: return global ? "Global feed" : "My feed";
      }
    }

    public static PickableNewsfeedSourceItemViewModel Photos = new PickableNewsfeedSourceItemViewModel(new PickableItem() { ID = -101L, Name = CommonResources.MainMenu_Photos.ToLowerInvariant(), Alias = CommonResources.MainMenu_Photos.ToLowerInvariant() });

    public static PickableNewsfeedSourceItemViewModel Videos = new PickableNewsfeedSourceItemViewModel(new PickableItem() { ID = -100L, Name = CommonResources.MainMenu_Videos.ToLowerInvariant(), Alias = CommonResources.MainMenu_Videos.ToLowerInvariant() });

    public static PickableNewsfeedSourceItemViewModel Friends = new PickableNewsfeedSourceItemViewModel(new PickableItem() { ID = -8L, Name = CommonResources.FriendsLowCase, Alias = CommonResources.FriendsLowCase });

    public static PickableNewsfeedSourceItemViewModel Groups = new PickableNewsfeedSourceItemViewModel(new PickableItem() { ID = -7L, Name = CommonResources.Groups.ToLowerInvariant(), Alias = CommonResources.Groups });

    public static ObservableCollection<PickableNewsfeedSourceItemViewModel> GetPredefinedNewsSources()
    {
      ObservableCollection<PickableNewsfeedSourceItemViewModel> observableCollection = new ObservableCollection<PickableNewsfeedSourceItemViewModel>();
      PickableNewsfeedSourceItemViewModel newsFeed = NewsSources.NewsFeed;
      observableCollection.Add(newsFeed);
      PickableNewsfeedSourceItemViewModel suggestions = NewsSources.Suggestions;
      observableCollection.Add(suggestions);
      return observableCollection;
    }

    public static ObservableCollection<PickableNewsfeedSourceItemViewModel> GetAllPredefinedNewsSources()
    {
      ObservableCollection<PickableNewsfeedSourceItemViewModel> observableCollection = new ObservableCollection<PickableNewsfeedSourceItemViewModel>();
      PickableNewsfeedSourceItemViewModel newsFeed = NewsSources.NewsFeed;
      observableCollection.Add(newsFeed);
      PickableNewsfeedSourceItemViewModel suggestions = NewsSources.Suggestions;
      observableCollection.Add(suggestions);
      return observableCollection;
    }

    public static ObservableCollection<PickableNewsfeedSourceItemViewModel> GetNewsSources(NewsFeedSectionsAndLists sectionsAndLists, PickableItem currentNewsSource = null)
    {
      return NewsSources.GetPredefinedNewsSources();
    }
  }
}
