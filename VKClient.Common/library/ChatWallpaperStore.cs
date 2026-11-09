using System;
using System.IO;
using System.IO.IsolatedStorage;
using System.Security.Cryptography;
using System.Text;
using VKClient.Common;
using VKClient.Common.Library;

namespace VKClient.Common.Library
{
    public sealed class ChatWallpaper
    {
        public byte[] Photo { get; set; }
        public double Dim { get; set; }
        public byte[] OriginalPhoto { get; set; }
        public double Zoom { get; set; }
        public double PanX { get; set; }
        public double PanY { get; set; }
        public bool Blur { get; set; }
    }
    // Private, per-account/server/peer local customization. No upload or API
    // request and no relationship to the chat's shared avatar.
    public static class ChatWallpaperStore
    {
        private const string Folder = "ChatWallpapers";
        private const string Pending = "ChatWallpaper.Pending";
        private const int MaximumBytes = 4 * 1024 * 1024;

        public static string GlobalKey(string origin, long owner) { return Key(origin, owner, long.MinValue, true); }
        public static string CurrentGlobalKey { get { return GlobalKey(OpenVKInstanceManager.Current.ApiOrigin, AppGlobalStateManager.Current.LoggedInUserId); } }
        public static bool Exists(string key)
        {
            string path = PathFor(key);
            using (var store = IsolatedStorageFile.GetUserStoreForApplication()) return store.FileExists(path) || store.FileExists(path + ".previous");
        }
        public static ChatWallpaper LoadEffective(string key)
        {
            return LoadWallpaper(key) ?? (key == CurrentGlobalKey ? null : LoadWallpaper(CurrentGlobalKey));
        }

        public static string Key(string origin, long owner, long peer, bool chat)
        {
            if (owner == 0 || peer == 0 || string.IsNullOrEmpty(origin)) return null;
            using (var hash = new SHA256Managed())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(origin.TrimEnd('/').ToLowerInvariant() + "\n" + owner + "\n" + (chat ? "chat:" : "user:") + peer))).Replace("-", "").ToLowerInvariant();
        }

        private static string PathFor(string key)
        {
            if (key == null || key.Length != 64) throw new ArgumentException("Invalid wallpaper key");
            foreach (char c in key) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) throw new ArgumentException("Invalid wallpaper key");
            return Folder + "/" + key + ".jpg";
        }

        public static void BeginSelection(string key)
        {
            PathFor(key);
            var settings = IsolatedStorageSettings.ApplicationSettings;
            settings[Pending] = key;
            settings[Pending + ".Owner"] = AppGlobalStateManager.Current.LoggedInUserId;
            settings[Pending + ".Origin"] = OpenVKInstanceManager.Current.ApiOrigin;
            settings.Save();
        }

        public static bool TakeSelection(out string key)
        {
            key = null;
            var settings = IsolatedStorageSettings.ApplicationSettings;
            if (!settings.Contains(Pending)) return false;
            long owner; string origin;
            settings.TryGetValue(Pending, out key);
            settings.TryGetValue(Pending + ".Owner", out owner);
            settings.TryGetValue(Pending + ".Origin", out origin);
            settings.Remove(Pending); settings.Remove(Pending + ".Owner"); settings.Remove(Pending + ".Origin"); settings.Save();
            if (owner != AppGlobalStateManager.Current.LoggedInUserId || origin != OpenVKInstanceManager.Current.ApiOrigin) key = null;
            return true;
        }

        public static byte[] Load(string key)
        {
            var wallpaper = LoadWallpaper(key);
            return wallpaper == null ? null : wallpaper.Photo;
        }

        public static ChatWallpaper LoadWallpaper(string key)
        {
            string path = PathFor(key);
            using (var store = IsolatedStorageFile.GetUserStoreForApplication())
            {
                if (!store.FileExists(path) && store.FileExists(path + ".previous")) store.MoveFile(path + ".previous", path);
                if (!store.FileExists(path)) return null;
                using (var input = store.OpenFile(path, FileMode.Open, FileAccess.Read))
                {
                    if (input.Length <= 0 || input.Length > MaximumBytes * 2 + 64) throw new IOException("Invalid wallpaper size");
                    using (var output = new MemoryStream())
                    {
                        input.CopyTo(output);
                        byte[] data = output.ToArray();
                        // Preserve existing 5.39 JPEG wallpapers. New records
                        // commit dim and image together, never in separate files.
                        if (data.Length > 2 && data[0] == 255 && data[1] == 216) return new ChatWallpaper { Photo = data, Dim = .48, Zoom = 1 };
                        using (var reader = new BinaryReader(new MemoryStream(data)))
                        {
                            if (reader.ReadInt32() != 0x574b564f) throw new IOException("Invalid wallpaper record");
                            int version = reader.ReadInt32();
                            if (version != 1 && version != 2) throw new IOException("Invalid wallpaper version");
                            double dim = reader.ReadDouble(); int length = reader.ReadInt32();
                            if (double.IsNaN(dim) || dim < 0 || dim > .85 || length < 3 || length > MaximumBytes || length > data.Length - 20) throw new IOException("Invalid wallpaper metadata");
                            var wallpaper = new ChatWallpaper { Photo = reader.ReadBytes(length), Dim = dim, Zoom = 1 };
                            if (version == 2)
                            {
                                int originalLength = reader.ReadInt32();
                                if (originalLength < 3 || originalLength > MaximumBytes || originalLength > reader.BaseStream.Length - reader.BaseStream.Position - 25) throw new IOException("Invalid original photo");
                                wallpaper.OriginalPhoto = reader.ReadBytes(originalLength);
                                wallpaper.Zoom = reader.ReadDouble(); wallpaper.PanX = reader.ReadDouble(); wallpaper.PanY = reader.ReadDouble(); wallpaper.Blur = reader.ReadBoolean();
                                ValidateEditing(wallpaper.OriginalPhoto, wallpaper.Zoom, wallpaper.PanX, wallpaper.PanY);
                            }
                            if (reader.BaseStream.Position != reader.BaseStream.Length) throw new IOException("Trailing wallpaper data");
                            return wallpaper;
                        }
                    }
                }
            }
        }

        public static void Save(string key, byte[] bytes)
        {
            Save(key, bytes, .48);
        }

        public static void Save(string key, byte[] bytes, double dim)
        {
            Save(key, bytes, dim, bytes, 1, 0, 0, false);
        }

        private static void ValidateEditing(byte[] original, double zoom, double panX, double panY)
        {
            if (original == null || original.Length < 3 || original.Length > MaximumBytes || original[0] != 255 || original[1] != 216 ||
                double.IsNaN(zoom) || zoom < 1 || zoom > 3 || double.IsNaN(panX) || Math.Abs(panX) > 1 || double.IsNaN(panY) || Math.Abs(panY) > 1)
                throw new ArgumentException("Invalid wallpaper editing metadata");
        }

        public static void Save(string key, byte[] bytes, double dim, byte[] original, double zoom, double panX, double panY, bool blur)
        {
            string path = PathFor(key);
            if (bytes == null || bytes.Length < 3 || bytes.Length > MaximumBytes || bytes[0] != 255 || bytes[1] != 216)
                throw new ArgumentException("Invalid wallpaper JPEG");
            if (double.IsNaN(dim) || dim < 0 || dim > .85) throw new ArgumentException("Invalid wallpaper dim");
            ValidateEditing(original, zoom, panX, panY);
            using (var record = new MemoryStream())
            using (var writer = new BinaryWriter(record))
            {
                writer.Write(0x574b564f); writer.Write(2); writer.Write(dim); writer.Write(bytes.Length); writer.Write(bytes);
                writer.Write(original.Length); writer.Write(original); writer.Write(zoom); writer.Write(panX); writer.Write(panY); writer.Write(blur);
                WriteAtomic(path, record.ToArray());
            }
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            using (var store = IsolatedStorageFile.GetUserStoreForApplication())
            {
                if (!store.DirectoryExists(Folder)) store.CreateDirectory(Folder);
                using (var output = store.OpenFile(path + ".pending", FileMode.Create, FileAccess.Write)) { output.Write(bytes, 0, bytes.Length); output.Flush(); }
                if (store.FileExists(path + ".previous"))
                {
                    if (!store.FileExists(path)) store.MoveFile(path + ".previous", path);
                    else store.DeleteFile(path + ".previous");
                }
                if (store.FileExists(path)) store.MoveFile(path, path + ".previous");
                try { store.MoveFile(path + ".pending", path); }
                catch { if (!store.FileExists(path) && store.FileExists(path + ".previous")) store.MoveFile(path + ".previous", path); throw; }
                // Keep the previous generation until the next save/reset. A
                // terminated app between moves can recover it on opening.
            }
        }

        public static void SaveDraft(string key, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 3 || bytes.Length > MaximumBytes) throw new ArgumentException("Invalid photo draft");
            WriteAtomic(PathFor(key) + ".draft", bytes);
        }
        public static byte[] LoadDraft(string key)
        {
            string path = PathFor(key) + ".draft";
            using (var store = IsolatedStorageFile.GetUserStoreForApplication())
            {
                if (!store.FileExists(path) && store.FileExists(path + ".previous")) store.MoveFile(path + ".previous", path);
                if (!store.FileExists(path)) return null;
                using (var stream = store.OpenFile(path, FileMode.Open, FileAccess.Read))
                using (var output = new MemoryStream()) { if (stream.Length > MaximumBytes) return null; stream.CopyTo(output); return output.ToArray(); }
            }
        }
        public static void ClearDraft(string key)
        {
            string path = PathFor(key) + ".draft";
            using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                foreach (string suffix in new[] { "", ".previous", ".pending" }) if (store.FileExists(path + suffix)) store.DeleteFile(path + suffix);
        }

        public static void Reset(string key)
        {
            string path = PathFor(key);
            using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                foreach (string suffix in new[] { "", ".previous", ".pending" })
                    if (store.FileExists(path + suffix)) store.DeleteFile(path + suffix);
        }
    }
}
