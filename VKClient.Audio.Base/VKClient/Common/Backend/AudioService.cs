using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using VKClient.Audio.Base;
using VKClient.Audio.Base.DataObjects;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.Utils;

namespace VKClient.Common.Backend
{
    public class AudioService
    {
        private static readonly Func<string, List<AudioObj>> _deserializeAudioList = (Func<string, List<AudioObj>>)(jsonStr => JsonConvert.DeserializeObject<GenericRoot<VKList<AudioObj>>>(jsonStr).response.items);
        private Dictionary<string, string> _cachedResults = new Dictionary<string, string>();
        private static AudioService _instance;

        public static AudioService Instance
        {
            get
            {
                return AudioService._instance ?? (AudioService._instance = new AudioService());
            }
        }

        private AudioService()
        {
        }

        public void GetLyrics(long lyrics_id, Action<BackendResult<Lyrics, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["lyrics_id"] = lyrics_id.ToString();
            VKRequestsDispatcher.DispatchRequestToVK<Lyrics>("audio.getLyrics", parameters, callback, null, false, true, new CancellationToken?(), null);
        }

        public void GetAllTracksAndAlbums(long userOrGroupId, bool isGroup, Action<BackendResult<AudioData, ResultCode>> callback)
        {
            // OpenVK's execute is unavailable on some instances (e.g. VepurOVK),
            // so compose the same data from the two public audio methods.
            AudioData data = new AudioData() { AllAlbums = new List<AudioAlbum>(), AllTracks = new List<AudioObj>() };
            int remaining = 2;
            Action completed = () => { if (--remaining == 0) callback(new BackendResult<AudioData, ResultCode>(ResultCode.Succeeded, data)); };
            this.GetAllAudio(res => { if (res.ResultCode == ResultCode.Succeeded && res.ResultData != null) data.AllTracks = res.ResultData; completed(); },
                userOrGroupId, isGroup, null, 0, VKConstants.TracksReadCount);
            this.GetUserAlbums(res =>
            {
                if (res.ResultCode == ResultCode.Succeeded && res.ResultData != null)
                {
                    data.AllAlbums = res.ResultData.Data;
                    data.AllAlbumsCount = res.ResultData.TotalCount;
                }
                completed();
            }, userOrGroupId, isGroup, 0, VKConstants.AlbumsReadCount);
        }

        public void MoveToAlbum(List<string> audioIds, long albumId, Action<BackendResult<object, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["audio_ids"] = audioIds.GetCommaSeparated(",");
            parameters["album_id"] = albumId.ToString();
            VKRequestsDispatcher.DispatchRequestToVK<object>("audio.moveToAlbum", parameters, callback, null, false, true, new CancellationToken?(), null);
        }

        public void EditAlbum(long albumId, string albumName, Action<BackendResult<object, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["title"] = albumName;
            parameters["album_id"] = albumId.ToString();
            VKRequestsDispatcher.DispatchRequestToVK<object>("audio.editAlbum", parameters, callback, null, false, true, new CancellationToken?(), null);
        }

        public void DeleteAlbum(long albumId, Action<BackendResult<object, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["album_id"] = albumId.ToString();
            VKRequestsDispatcher.DispatchRequestToVK<object>("audio.deleteAlbum", parameters, callback, null, false, true, new CancellationToken?(), null);
        }

        public void CreateAlbum(string albumName, Action<BackendResult<AudioAlbum, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["title"] = albumName;
            VKRequestsDispatcher.DispatchRequestToVK<AudioAlbum>("audio.addAlbum", parameters, callback, (Func<string, AudioAlbum>)(jsonStr =>
            {
                VKRequestsDispatcher.GenericRoot<long> root = JsonConvert.DeserializeObject<VKRequestsDispatcher.GenericRoot<long>>(jsonStr);
                return new AudioAlbum()
                {
                    album_id = root != null ? root.response : 0L,
                    owner_id = AppGlobalStateManager.Current.LoggedInUserId,
                    title = albumName
                };
            }), false, true, new CancellationToken?(), null);
        }

        public void GetAllAudio(Action<BackendResult<List<AudioObj>, ResultCode>> callback, long? userOrGroupId = null, bool isGroup = false, long? albumId = null, int offset = 0, int count = 0)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            if (userOrGroupId.HasValue)
                parameters["owner_id"] = (isGroup ? -Math.Abs(userOrGroupId.Value) : userOrGroupId.Value).ToString();
            if (albumId.HasValue)
                parameters["album_id"] = albumId.Value.ToString();
            parameters["offset"] = offset.ToString();
            parameters["count"] = (count > 0 ? count : VKConstants.TracksReadCount).ToString();
            VKRequestsDispatcher.DispatchRequestToVK<List<AudioObj>>("audio.get", parameters, callback, AudioService._deserializeAudioList, false, true, new CancellationToken?(), null);
        }

        public void GetRecommended(long uid, long offset, long count, Action<BackendResult<List<AudioObj>, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["user_id"] = uid.ToString();
            parameters["offset"] = offset.ToString();
            parameters["count"] = count.ToString();
            VKRequestsDispatcher.DispatchRequestToVK<List<AudioObj>>("audio.getRecommendations", parameters, callback, AudioService._deserializeAudioList, false, true, new CancellationToken?(), null);
        }

        public void GetPopular(int offset, int count, Action<BackendResult<List<AudioObj>, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["offset"] = offset.ToString();
            parameters["count"] = count.ToString();
            VKRequestsDispatcher.DispatchRequestToVK<List<AudioObj>>("audio.getPopular", parameters, callback, AudioService._deserializeAudioList, false, true, new CancellationToken?(), null);
        }

        public void GetUserAlbums(Action<BackendResult<ListResponse<AudioAlbum>, ResultCode>> callback, long? userOrGroupId = null, bool isGroup = false, int offset = 0, int count = 0)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            if (userOrGroupId.HasValue)
                parameters["owner_id"] = (isGroup ? -Math.Abs(userOrGroupId.Value) : userOrGroupId.Value).ToString();
            parameters["offset"] = offset.ToString();
            parameters["count"] = count == 0 ? VKConstants.AlbumsReadCount.ToString() : count.ToString();
            VKRequestsDispatcher.DispatchRequestToVK<ListResponse<AudioAlbum>>("audio.getAlbums", parameters, callback, (Func<string, ListResponse<AudioAlbum>>)(jsonStr =>
            {
                GenericRoot<VKList<AudioAlbum>> genericRoot = JsonConvert.DeserializeObject<GenericRoot<VKList<AudioAlbum>>>(jsonStr);
                return new ListResponse<AudioAlbum>()
                {
                    Data = genericRoot.response.items,
                    TotalCount = genericRoot.response.count
                };
            }), false, true, new CancellationToken?(), null);
        }

        public void GetAllAudioForUser(long uid, long guid, long albumId, List<long> aids, int count, int offset, Action<BackendResult<List<AudioObj>, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            long ownerId = guid != 0L ? -Math.Abs(guid) : uid;
            if (ownerId != 0L)
                parameters["owner_id"] = ownerId.ToString();
            if (albumId != 0L)
                parameters["album_id"] = albumId.ToString();
            if (aids != null && aids.Count > 0)
            {
                if (ownerId != 0L)
                {
                    List<string> audioIds = new List<string>();
                    foreach (long aid in aids)
                        audioIds.Add(ownerId.ToString() + "_" + aid.ToString());
                    parameters["audio_ids"] = audioIds.GetCommaSeparated(",");
                }
                else
                    parameters["audio_ids"] = aids.GetCommaSeparated();
            }
            parameters["offset"] = offset.ToString();
            parameters["count"] = (count > 0 ? count : VKConstants.TracksReadCount).ToString();
            VKRequestsDispatcher.DispatchRequestToVK<List<AudioObj>>("audio.get", parameters, callback, AudioService._deserializeAudioList, false, true, new CancellationToken?(), null);
        }

        public void SearchTracks(string query, int offset, int count, Action<BackendResult<VKList<AudioObj>, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["q"] = query;
            parameters["count"] = count.ToString();
            parameters["offset"] = offset.ToString();
            // OpenVK rejects the legacy search_own flag. Own tracks are part
            // of the normal search result when they match the query.
            VKRequestsDispatcher.DispatchRequestToVK<VKList<AudioObj>>("audio.search", parameters, callback, null, false, true, new CancellationToken?(), null);
        }

        public void GetAudio(List<string> ids, Action<BackendResult<List<AudioObj>, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["audios"] = ids.GetCommaSeparated(",");
            VKRequestsDispatcher.DispatchRequestToVK<List<AudioObj>>("audio.getById", parameters, callback, AudioService._deserializeAudioList, false, true, new CancellationToken?(), null);
        }

        public void AddAudio(long ownerId, long aid, Action<BackendResult<long, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters["owner_id"] = ownerId.ToString();
            parameters["audio_id"] = aid.ToString();
            VKRequestsDispatcher.DispatchRequestToVK<long>("audio.add", parameters, callback, (Func<string, long>)(jsonStr =>
            {
                string prettyId = JsonConvert.DeserializeObject<VKRequestsDispatcher.GenericRoot<string>>(jsonStr).response;
                string[] strArray = (prettyId ?? "").Split('_');
                long num = 0;
                if (strArray.Length > 0)
                    long.TryParse(strArray[strArray.Length - 1], out num);
                return num;
            }), false, true, new CancellationToken?(), null);
        }

        public void DeleteAudios(List<AudioObj> tracks)
        {
            if (tracks == null || tracks.Count == 0)
                return;

            foreach (AudioObj track in tracks)
            {
                if (track == null)
                    continue;
                Dictionary<string, string> parameters = new Dictionary<string, string>();
                parameters["audio_id"] = track.aid.ToString();
                parameters["owner_id"] = track.owner_id.ToString();
                VKRequestsDispatcher.DispatchRequestToVK<long>("audio.delete", parameters, res => { }, null, false, true, new CancellationToken?(), null);
            }
        }

        public void ReorderAudio(long aid, long oid, long album_id, long after, long before, Action<BackendResult<long, ResultCode>> callback)
        {
            // OpenVK currently exposes playlist add/remove/move methods but no
            // audio.reorder equivalent. Keep the client-side order responsive
            // and do not generate a guaranteed API error.
            if (callback != null)
                callback(new BackendResult<long, ResultCode>(ResultCode.Succeeded, 1L));
        }

        public void StatusSet(string text, string audio, Action<BackendResult<long, ResultCode>> callback)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(audio))
            {
                // OpenVK has no VKScript/execute. The current handler accepts the
                // legacy ownerId_audioId descriptor and requires explicit targets.
                parameters["audio"] = audio;
                parameters["target_ids"] = AppGlobalStateManager.Current.LoggedInUserId.ToString();
                VKRequestsDispatcher.DispatchRequestToVK<long>("audio.setBroadcast", parameters, callback, (Func<string, long>)(res => 1L), false, true, new CancellationToken?(), null);
            }
            else
            {
                parameters["text"] = text;
                VKRequestsDispatcher.DispatchRequestToVK<long>("status.set", parameters, callback, null, false, true, new CancellationToken?(), null);
            }
        }

        public void ResetBroadcast(Action<BackendResult<long, ResultCode>> callback)
        {
            // Current OpenVK requires both audio and target_ids for setBroadcast
            // and exposes no separate clear method. Avoid an invalid empty request.
            if (callback != null)
                callback(new BackendResult<long, ResultCode>(ResultCode.Succeeded, 1L));
        }

        public void EditAudio(long ownerId, long id, string artist, string title, Action<BackendResult<long, ResultCode>> callback)
        {
            VKRequestsDispatcher.DispatchRequestToVK<long>("audio.edit", new Dictionary<string, string>()
      {
        {
          "owner_id",
          ownerId.ToString()
        },
        {
          "audio_id",
          id.ToString()
        },
        {
          "artist",
          artist
        },
        {
          "title",
          title
        }
      }, callback, null, false, true, new CancellationToken?(), null);
        }

        public void UploadAudio(Stream stream, string artist, string title, Action<BackendResult<AudioObj, ResultCode>> callback, Action<double> progressCallback = null, Cancellation cancellation = null)
        {
            VKRequestsDispatcher.DispatchRequestToVK<VKClient.Common.Backend.DataObjects.UploadServerAddress>("audio.getUploadServer", new Dictionary<string, string>(), (Action<BackendResult<VKClient.Common.Backend.DataObjects.UploadServerAddress, ResultCode>>)(result =>
            {
                if (result.ResultCode != ResultCode.Succeeded)
                    callback(new BackendResult<AudioObj, ResultCode>(result.ResultCode));
                else
                    JsonWebRequest.Upload(result.ResultData.upload_url, stream, "file", "audio", (Action<JsonResponseData>)(uploadResult =>
                    {
                        if (!uploadResult.IsSucceeded)
                        {
                            callback(new BackendResult<AudioObj, ResultCode>(ResultCode.UploadingFailed));
                        }
                        else
                        {
                            UploadResponseData uploadResponseData = JsonConvert.DeserializeObject<UploadResponseData>(uploadResult.JsonString);
                            VKRequestsDispatcher.DispatchRequestToVK<AudioObj>("audio.save", new Dictionary<string, string>()
              {
                {
                  "server",
                  uploadResponseData.server
                },
                {
                  "audio",
                  uploadResponseData.audio
                },
                {
                  "hash",
                  uploadResponseData.hash
                },
                {
                  "artist",
                  artist
                },
                {
                  "title",
                  title
                }
              }, callback, null, false, true, new CancellationToken?(), null);
                        }
                    }), "track.mp3", progressCallback, cancellation);
            }), (Func<string, VKClient.Common.Backend.DataObjects.UploadServerAddress>)null, false, true, new CancellationToken?(), null);
        }

        public void GetAlbumArtwork(string search, Action<BackendResult<AlbumArtwork, ResultCode>> callback)
        {
            string format = "https://itunes.apple.com/search?media=music&limit=1&version=2&term={0}";
            search = HttpUtility.UrlEncode(search);
            if (this._cachedResults.ContainsKey(search))
                callback(new BackendResult<AlbumArtwork, ResultCode>(ResultCode.Succeeded, new AlbumArtwork()
                {
                    ImageUri = this._cachedResults[search]
                }));
            else
                JsonWebRequest.SendHTTPRequestAsync(string.Format(format, search), (Action<JsonResponseData>)(resp =>
                {
                    if (resp.IsSucceeded)
                    {
                        try
                        {
                            AudioService.ItunesList itunesList = JsonConvert.DeserializeObject<AudioService.ItunesList>(resp.JsonString);
                            if (!itunesList.results.IsNullOrEmpty())
                            {
                                AudioService.ItunesAlbumArt result = itunesList.results[0];
                                AlbumArtwork albArt = new AlbumArtwork()
                                {
                                    ImageUri = result.artworkUrl100.Replace("100x100", "600x600")
                                };
                                Execute.ExecuteOnUIThread((Action)(() =>
                                {
                                    this._cachedResults[search] = albArt.ImageUri;
                                    callback(new BackendResult<AlbumArtwork, ResultCode>(ResultCode.Succeeded, albArt));
                                }));
                            }
                            else
                                callback(new BackendResult<AlbumArtwork, ResultCode>(ResultCode.Succeeded, new AlbumArtwork()));
                        }
                        catch (Exception)
                        {
                            callback(new BackendResult<AlbumArtwork, ResultCode>(ResultCode.UnknownError));
                        }
                    }
                    else
                        callback(new BackendResult<AlbumArtwork, ResultCode>(ResultCode.CommunicationFailed));
                }), null);
        }

        private class ItunesAlbumArt
        {
            public string artworkUrl100 { get; set; }
        }

        private class ItunesList
        {
            public int resultCount { get; set; }

            public List<AudioService.ItunesAlbumArt> results { get; set; }
        }
    }
}
