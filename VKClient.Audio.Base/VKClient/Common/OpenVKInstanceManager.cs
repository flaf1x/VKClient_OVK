using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;

namespace VKClient.Common
{
    public sealed class OpenVKInstance
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string WebOrigin { get; private set; }
        public string ApiOrigin { get; private set; }

        public OpenVKInstance(string id, string name, string webOrigin, string apiOrigin)
        {
            this.Id = id;
            this.Name = name;
            this.WebOrigin = OpenVKInstanceManager.NormalizeOrigin(webOrigin);
            this.ApiOrigin = OpenVKInstanceManager.NormalizeOrigin(apiOrigin);
        }

        public override string ToString()
        {
            return this.Name;
        }
    }

    /// <summary>
    /// Single source of truth for the selected OpenVK server.  Web and API
    /// origins are deliberately separate: mirrors commonly expose /method on
    /// the web host, while the main instances use an api subdomain.
    /// </summary>
    public static class OpenVKInstanceManager
    {
        private const string IdKey = "OpenVK.Instance.Id";
        private const string WebKey = "OpenVK.Instance.WebOrigin";
        private const string ApiKey = "OpenVK.Instance.ApiOrigin";
        private static readonly OpenVKInstance[] _presets = new OpenVKInstance[]
        {
            new OpenVKInstance("openvk.org", "openvk.org", "https://openvk.org", "https://api.openvk.org"),
            new OpenVKInstance("openvk.xyz", "openvk.xyz (Cloudflare)", "https://openvk.xyz", "https://openvk.xyz"),
            new OpenVKInstance("vepurovk.fun", "vepurovk.fun", "https://vepurovk.fun", "https://api.vepurovk.fun"),
            new OpenVKInstance("vepurovk.xyz", "vepurovk.xyz (Cloudflare)", "https://vepurovk.xyz", "https://vepurovk.xyz")
        };
        private static OpenVKInstance _current;

        public static IList<OpenVKInstance> Presets
        {
            get { return _presets; }
        }

        public static OpenVKInstance Current
        {
            get
            {
                if (_current == null)
                    _current = Load();
                return _current;
            }
        }

        public static bool TryCreateCustom(string value, out OpenVKInstance instance, out string error)
        {
            instance = null;
            error = null;
            string origin = NormalizeOrigin(value);
            Uri uri;
            if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || string.IsNullOrEmpty(uri.Host))
            {
                error = "Enter an address like https://example.org";
                return false;
            }
            instance = new OpenVKInstance("custom", uri.Host, origin, origin);
            return true;
        }

        public static bool Select(OpenVKInstance instance)
        {
            if (instance == null)
                return false;
            bool changed = !OriginsEqual(Current.WebOrigin, instance.WebOrigin) ||
                           !OriginsEqual(Current.ApiOrigin, instance.ApiOrigin);
            _current = instance;
            IsolatedStorageSettings settings = IsolatedStorageSettings.ApplicationSettings;
            settings[IdKey] = instance.Id;
            settings[WebKey] = instance.WebOrigin;
            settings[ApiKey] = instance.ApiOrigin;
            settings.Save();
            return changed;
        }

        public static string ResolveWebUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;
            Uri absolute;
            if (Uri.TryCreate(url, UriKind.Absolute, out absolute))
                return absolute.AbsoluteUri;
            Uri root = new Uri(Current.WebOrigin + "/", UriKind.Absolute);
            Uri resolved;
            return Uri.TryCreate(root, url.TrimStart('/'), out resolved) ? resolved.AbsoluteUri : url;
        }

        public static bool IsCurrentInstanceUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url.IndexOf("://", StringComparison.Ordinal) >= 0 ? url : "https://" + url,
                               UriKind.Absolute, out uri))
                return false;
            return HostMatches(uri.Host, new Uri(Current.WebOrigin).Host) ||
                   HostMatches(uri.Host, new Uri(Current.ApiOrigin).Host);
        }

        internal static string NormalizeOrigin(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length == 0)
                return string.Empty;
            if (value.IndexOf("://", StringComparison.Ordinal) < 0)
                value = "https://" + value;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
                return string.Empty;
            return uri.Scheme + "://" + uri.Authority;
        }

        private static OpenVKInstance Load()
        {
            IsolatedStorageSettings settings = IsolatedStorageSettings.ApplicationSettings;
            string id = settings.Contains(IdKey) ? settings[IdKey] as string : null;
            string web = settings.Contains(WebKey) ? settings[WebKey] as string : null;
            string api = settings.Contains(ApiKey) ? settings[ApiKey] as string : null;
            if (!string.IsNullOrEmpty(web) && !string.IsNullOrEmpty(api))
            {
                foreach (OpenVKInstance preset in _presets)
                    if (preset.Id == id && OriginsEqual(preset.WebOrigin, web) && OriginsEqual(preset.ApiOrigin, api))
                        return preset;
                return new OpenVKInstance("custom", new Uri(web).Host, web, api);
            }
            return _presets[0];
        }

        private static bool HostMatches(string host, string expected)
        {
            return host.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("." + expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool OriginsEqual(string first, string second)
        {
            return string.Equals(NormalizeOrigin(first), NormalizeOrigin(second), StringComparison.OrdinalIgnoreCase);
        }
    }
}
