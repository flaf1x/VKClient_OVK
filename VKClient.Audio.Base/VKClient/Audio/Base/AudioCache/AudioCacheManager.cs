using Microsoft.Phone.BackgroundAudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VKClient.Audio.Base.BLExtensions;
using VKClient.Common.Backend;
using VKClient.Common.Backend.DataObjects;
using VKClient.Common.AudioManager;
using VKClient.Common.Framework;
using VKClient.Common.Library;
using VKClient.Common.Library.Events;
using VKClient.Common.Utils;

namespace VKClient.Audio.Base.AudioCache
{
    public class AudioCacheManager : IBinarySerializable, IHandle<AudioPlayerStateChanged>, IHandle
    {
        private List<InProgressDownloadInfo> _inProgressDownloads = new List<InProgressDownloadInfo>();
        private Dictionary<string, string> _downloadedDict = new Dictionary<string, string>();
        private List<AudioObj> _downloadedList = new List<AudioObj>();
        private static AudioCacheManager _instance;
        private bool _isDownloading;
        private bool _cacheChanging;
        private RemoteFileInfo _autoCacheCandidate;
        private HashSet<string> _bulkDownloadIds = new HashSet<string>();
        private int _bulkDownloadTotal;
        private int _bulkDownloadCompleted;
        private int _bulkDownloadFailed;
        private InProgressDownloadInfo _currentDownload;
        private Cancellation _currentDownloadCancellation;

        public static AudioCacheManager Instance
        {
            get
            {
                if (AudioCacheManager._instance == null)
                {
                    AudioCacheManager._instance = new AudioCacheManager();
                    CacheManager.TryDeserialize((IBinarySerializable)AudioCacheManager._instance, "AudioCacheManager", CacheManager.DataType.CachedData);
                }
                return AudioCacheManager._instance;
            }
        }

        public List<AudioObj> CachedListForCurrentUser
        {
            get
            {
                List<AudioObj> list = this._downloadedList.ToList<AudioObj>();
                list.Reverse();
                return list;
            }
        }

        public bool NetworkStatusInfo { get; set; }

        public AudioDownloadQueueProgress BulkDownloadProgress
        {
            get
            {
                return this.CreateBulkDownloadProgress(0.0);
            }
        }

        public AudioCacheManager()
        {
            EventAggregator.Current.Subscribe(this);
        }

        public void Save()
        {
            CacheManager.TrySerialize(this, "AudioCacheManager", false, CacheManager.DataType.CachedData);
        }

        public string GetLocalFileForUniqueId(string uniqueId)
        {
            if (this._downloadedDict.ContainsKey(uniqueId))
                return this._downloadedDict[uniqueId];
            return null;
        }

        public void EnsureCachingInRunning()
        {
            this.DownloadNextOneIfNeeded();
        }

        public async Task ClearCache(IEnumerable<string> idsForRemoving = null)
        {
            if (this._cacheChanging)
                return;
            this._cacheChanging = true;
            foreach (KeyValuePair<string, string> keyValuePair in new Dictionary<string, string>((IDictionary<string, string>)this._downloadedDict))
            {
                KeyValuePair<string, string> kvp = keyValuePair;
                if (idsForRemoving == null || idsForRemoving.Contains<string>(kvp.Key))
                {
                    if (await CacheManager.TryDeleteAsync(kvp.Value))
                    {
                        this._downloadedDict.Remove(kvp.Key);
                        AudioObj audioObj = this._downloadedList.FirstOrDefault<AudioObj>((Func<AudioObj, bool>)(a => a.UniqueId == kvp.Key));
                        if (audioObj != null)
                            this._downloadedList.Remove(audioObj);
                    }
                }
            }
            this.Save();
            this._cacheChanging = false;
        }

        public void Download(AudioObj audio)
        {
            if (audio == null || string.IsNullOrWhiteSpace(audio.url) ||
                !audio.url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return;
            this.AddFileToDownload(new List<RemoteFileInfo>()
            {
                new RemoteFileInfo(audio)
            });
        }

        public void DownloadAll(IEnumerable<AudioObj> audios)
        {
            if (audios == null)
                return;
            List<RemoteFileInfo> files = audios
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.url) &&
                    a.url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .GroupBy(a => a.UniqueId)
                .Select(g => new RemoteFileInfo(g.First()))
                .Where(f => !this._downloadedDict.ContainsKey(f.UniqueId))
                .ToList();
            Execute.ExecuteOnUIThread((Action)(() =>
            {
                this._bulkDownloadIds = new HashSet<string>(files.Select(f => f.UniqueId));
                this._bulkDownloadTotal = this._bulkDownloadIds.Count;
                this._bulkDownloadCompleted = 0;
                this._bulkDownloadFailed = 0;
                this.PublishBulkDownloadProgress(0.0);
                this.AddFileToDownload(files);
            }));
        }

        public void CancelBulkDownload()
        {
            Execute.ExecuteOnUIThread((Action)(() =>
            {
                if (this._bulkDownloadIds.Count == 0)
                    return;
                HashSet<string> idsToCancel = new HashSet<string>(this._bulkDownloadIds);
                if (this._currentDownloadCancellation != null && this._currentDownload != null &&
                    idsToCancel.Contains(this._currentDownload.RemoteFile.UniqueId))
                    this._currentDownloadCancellation.Set();
                this._inProgressDownloads.RemoveAll(d => d != this._currentDownload &&
                    d != null && d.RemoteFile != null && idsToCancel.Contains(d.RemoteFile.UniqueId));
                this._bulkDownloadIds.Clear();
                this._bulkDownloadTotal = 0;
                this._bulkDownloadCompleted = 0;
                this._bulkDownloadFailed = 0;
                this.Save();
                this.PublishBulkDownloadProgress(0.0);
            }));
        }

        public void AddFileToDownload(List<RemoteFileInfo> remoteFileInfoList)
        {
            Execute.ExecuteOnUIThread((Action)(() =>
            {
                foreach (RemoteFileInfo remoteFileInfo1 in remoteFileInfoList)
                {
                    RemoteFileInfo remoteFileInfo = remoteFileInfo1;
                    if (!this._downloadedDict.ContainsKey(remoteFileInfo.UniqueId))
                    {
                        InProgressDownloadInfo progressDownloadInfo1 = this._inProgressDownloads.FirstOrDefault<InProgressDownloadInfo>((Func<InProgressDownloadInfo, bool>)(inPr => inPr.RemoteFile.UniqueId == remoteFileInfo.UniqueId));
                        
                        if (progressDownloadInfo1 != null)
                        {
                            progressDownloadInfo1.RemoteFile = remoteFileInfo;
                            progressDownloadInfo1.LastDownloadResult = DownloadResult.OK;
                            progressDownloadInfo1.DownloadedToByte = -1L;
                            progressDownloadInfo1.Length = -1L;
                        }
                        else
                            this._inProgressDownloads.Add(new InProgressDownloadInfo(remoteFileInfo));
                    }
                }
                this.Save();
                this.DownloadNextOneIfNeeded();
            }));
        }

        private void DownloadNextOneIfNeeded()
        {
            if (VKClient.Common.Library.NetworkStatusInfo.Instance.NetworkStatus == NetworkStatus.MobileRestricted)
                return;
            Execute.ExecuteOnUIThread((Action)(() =>
            {
                if (this._isDownloading)
                    return;
                Logger.Instance.Info("AudioCacheManager.DownloadNextOneIfNeeded starting: InProgressDownload.Count == " + this._inProgressDownloads.Count);
                InProgressDownloadInfo currentDownload = this._inProgressDownloads.LastOrDefault<InProgressDownloadInfo>((Func<InProgressDownloadInfo, bool>)(ipd => ipd.LastDownloadResult != DownloadResult.UriNoLongerValid));
                if (currentDownload == null)
                {
                    Logger.Instance.Info("AudioCacheManager.DownloadNextOneIfNeeded: no files to download");
                }
                else
                {
                    Logger.Instance.Info("AudioCacheManager.DownloadNextOneIfNeeded: found download " + currentDownload.ToString());
                    this._isDownloading = true;
                    this._currentDownload = currentDownload;
                    this.PerformFullDownload(currentDownload, (Action)(() =>
                    {
                        Execute.ExecuteOnUIThread((Action)(() =>
                        {
                            DownloadResult lastDownloadResult = currentDownload.LastDownloadResult;
                            Logger.Instance.Info("AudioCacheManager: download completed with result " + lastDownloadResult);
                            this._inProgressDownloads.Remove(currentDownload);
                            if (lastDownloadResult == DownloadResult.OK)
                                this.ProcessFullyDownloadedFile(currentDownload);
                            if (this._bulkDownloadIds.Remove(currentDownload.RemoteFile.UniqueId))
                            {
                                ++this._bulkDownloadCompleted;
                                if (lastDownloadResult != DownloadResult.OK)
                                    ++this._bulkDownloadFailed;
                                this.PublishBulkDownloadProgress(0.0);
                            }
                            this._isDownloading = false;
                            this._currentDownload = null;
                            this._currentDownloadCancellation = null;
                            this.Save();
                            this.DownloadNextOneIfNeeded();
                        }));
                    }));
                }
            }));
        }

        private void ProcessFullyDownloadedFile(InProgressDownloadInfo currentDownload)
        {
            if (!string.IsNullOrWhiteSpace(currentDownload.RemoteFile.Title) ||
                !string.IsNullOrWhiteSpace(currentDownload.RemoteFile.Artist))
            {
                this.RegisterDownloadedFile(currentDownload, currentDownload.RemoteFile.ToAudioObj());
                return;
            }

            List<string> ids = new List<string>();
            ids.Add(currentDownload.RemoteFile.UniqueId);
            Action<BackendResult<List<AudioObj>, ResultCode>> callback = (Action<BackendResult<List<AudioObj>, ResultCode>>)(res =>
            {
                if (res.ResultCode != ResultCode.Succeeded)
                    return;
                if (res.ResultData == null || res.ResultData.Count == 0)
                    return;
                this.RegisterDownloadedFile(currentDownload, res.ResultData[0]);
            });
            AudioService.Instance.GetAudio(ids, callback);
        }

        private void RegisterDownloadedFile(InProgressDownloadInfo currentDownload, AudioObj audioObj)
        {
            this._downloadedDict[currentDownload.RemoteFile.UniqueId] = currentDownload.LocalFileId;
            audioObj.url = currentDownload.LocalFileId;
            this._downloadedList.RemoveAll(a => a.UniqueId == audioObj.UniqueId);
            this._downloadedList.Add(audioObj);
            this.Save();
            EventAggregator.Current.Publish(new AudioTrackDownloadProgress
            {
                Id = currentDownload.RemoteFile.UniqueId,
                Progress = 100f
            });
        }

        private void PerformFullDownload(InProgressDownloadInfo di, Action resultCallback)
        {
            CacheManager.EnsureCacheFolderExists();
            Stream destination = null;
            try
            {
                destination = CacheManager.GetStreamForWrite(di.LocalFileId);
                Cancellation cancellation = new Cancellation();
                this._currentDownloadCancellation = cancellation;
                JsonWebRequest.Download(di.RemoteFile.UriStr, destination, (Action<bool>)(succeeded =>
                {
                    long length = 0L;
                    try
                    {
                        length = destination.Position;
                        destination.Dispose();
                    }
                    catch
                    {
                    }
                    di.Length = length;
                    di.DownloadedToByte = length > 0L ? length - 1L : -1L;
                    di.LastDownloadResult = succeeded && length > 0L
                        ? DownloadResult.OK
                        : DownloadResult.Failed;
                    if (di.LastDownloadResult != DownloadResult.OK)
                        CacheManager.TryDeleteAsync(di.LocalFileId);
                    resultCallback();
                }), (Action<double>)(progress =>
                {
                    EventAggregator.Current.Publish(new AudioTrackDownloadProgress
                    {
                        Id = di.RemoteFile.UniqueId,
                        Progress = (float)progress
                    });
                    if (this._bulkDownloadIds.Contains(di.RemoteFile.UniqueId))
                        this.PublishBulkDownloadProgress(progress);
                }), cancellation);
            }
            catch (Exception ex)
            {
                if (destination != null)
                    destination.Dispose();
                di.LastDownloadResult = DownloadResult.Failed;
                Logger.Instance.Error("AudioCacheManager: failed to start download", ex);
                resultCallback();
            }
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(1);
            writer.WriteList<InProgressDownloadInfo>(this._inProgressDownloads, 10000);
            writer.WriteList<AudioObj>(this._downloadedList, 10000);
        }

        public void Read(BinaryReader reader)
        {
            reader.ReadInt32();
            this._inProgressDownloads = reader.ReadList<InProgressDownloadInfo>();
            this._downloadedList = reader.ReadList<AudioObj>();
            this._downloadedDict = new Dictionary<string, string>();
            foreach (AudioObj downloaded in this._downloadedList)
                this._downloadedDict[downloaded.UniqueId] = downloaded.url;
            this._bulkDownloadIds = new HashSet<string>(this._inProgressDownloads
                .Where(d => d != null && d.RemoteFile != null)
                .Select(d => d.RemoteFile.UniqueId));
            this._bulkDownloadTotal = this._bulkDownloadIds.Count;
            this._bulkDownloadCompleted = 0;
            this._bulkDownloadFailed = 0;
        }

        public void Handle(AudioPlayerStateChanged message)
        {
            AudioTrack currentTrack = BGAudioPlayerWrapper.Instance.Track;
            string currentId = currentTrack != null ? currentTrack.GetTagId() : null;

            if (this._autoCacheCandidate != null &&
                (currentId != this._autoCacheCandidate.UniqueId ||
                 message.PlayState == PlayState.TrackEnded ||
                 message.PlayState == PlayState.Stopped))
            {
                if (AppGlobalStateManager.Current.GlobalState.IsMusicCachingEnabled)
                    this.AddFileToDownload(new List<RemoteFileInfo>() { this._autoCacheCandidate });
                this._autoCacheCandidate = null;
            }

            if (message.PlayState != PlayState.Playing || currentTrack == null ||
                currentTrack.Source == null ||
                !currentTrack.Source.OriginalString.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return;

            Playlist playlist = PlaylistManager.LoadTracksFromIsolatedStorage(true);
            AudioObj audio = playlist.Tracks.FirstOrDefault(a => a.UniqueId == currentId);
            if (audio != null && audio.added)
                this._autoCacheCandidate = new RemoteFileInfo(audio);
        }

        private AudioDownloadQueueProgress CreateBulkDownloadProgress(double currentTrackProgress)
        {
            bool isActive = this._bulkDownloadIds.Count > 0;
            double progress = this._bulkDownloadTotal > 0
                ? (this._bulkDownloadCompleted + currentTrackProgress / 100.0) / this._bulkDownloadTotal
                : 1.0;
            return new AudioDownloadQueueProgress
            {
                Completed = this._bulkDownloadCompleted,
                Failed = this._bulkDownloadFailed,
                Total = this._bulkDownloadTotal,
                Progress = Math.Max(0.0, Math.Min(1.0, progress)),
                IsActive = isActive
            };
        }

        private void PublishBulkDownloadProgress(double currentTrackProgress)
        {
            EventAggregator.Current.Publish(this.CreateBulkDownloadProgress(currentTrackProgress));
        }
    }
}
