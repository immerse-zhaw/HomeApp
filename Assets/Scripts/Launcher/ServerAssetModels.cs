using System;

namespace Launcher
{
    [Serializable]
    public class ServerAssetList
    {
        public ServerAsset[] items;
    }

    [Serializable]
    public class ServerAsset
    {
        public string id;
        public string type;
        public string originalFilename;
        public string mime;
        public long sizeBytes;
        public string streamUrl;
        public string downloadUrl;
        public string universalMp4Url;
        public string[] thumbnails;
        public string[] tags;
        public bool locked;
        public bool hidden;
        public VideoSettings videoSettings;
    }

    [Serializable]
    public class VideoSettings
    {
        public string projection;
    }

    public static class ServerAssetUtils
    {
        public static string BuildAbsoluteUrl(string baseUrl, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            if (string.IsNullOrWhiteSpace(baseUrl)) return path;

            if (!path.StartsWith("/"))
                path = "/" + path;

            return baseUrl.TrimEnd('/') + path;
        }

        public static void ParseProjection(string projectionSetting, out string projection, out string stereo)
        {
            var value = (projectionSetting ?? string.Empty).ToUpperInvariant();
            projection = value.Contains("180") ? "180" : "360";

            if (value.Contains("TB") || value.Contains("TOPBOTTOM"))
            {
                stereo = "tb";
            }
            else if (value.Contains("SBS") || value.Contains("LR") || value.Contains("SIDEBYSIDE"))
            {
                stereo = "sbs";
            }
            else
            {
                stereo = "mono";
            }
        }
    }
}
