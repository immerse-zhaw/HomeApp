using System;
using System.IO;
using UnityEngine;

namespace Launcher
{
    public static class ContentCache
    {
        public static string Root => Path.Combine(Application.persistentDataPath, "HomeContentCache");

        public static string GetCachedPath(string category, string fileId, string url, string fallbackExtension)
        {
            string cacheDir = Path.Combine(Root, SanitizePathSegment(category));
            Directory.CreateDirectory(cacheDir);

            string key = !string.IsNullOrWhiteSpace(fileId) ? fileId : StableHash(url);
            string ext = GetExtension(url, fallbackExtension);
            return Path.Combine(cacheDir, SanitizePathSegment(key) + ext);
        }

        public static bool IsCached(string category, string fileId, string url, string fallbackExtension)
        {
            string path = GetCachedPath(category, fileId, url, fallbackExtension);
            try
            {
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static string GetTempPath(string finalPath)
        {
            return finalPath + ".part";
        }

        private static string GetExtension(string url, string fallback)
        {
            try
            {
                string ext = Path.GetExtension(new Uri(url).LocalPath);
                return string.IsNullOrEmpty(ext) ? fallback : ext;
            }
            catch
            {
                return fallback;
            }
        }

        private static string StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                string input = value ?? string.Empty;
                for (int i = 0; i < input.Length; i++)
                {
                    hash ^= input[i];
                    hash *= 16777619;
                }
                return hash.ToString("x8");
            }
        }

        private static string SanitizePathSegment(string value)
        {
            string input = string.IsNullOrWhiteSpace(value) ? "unknown" : value;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                input = input.Replace(c, '_');
            }
            return input;
        }
    }
}
