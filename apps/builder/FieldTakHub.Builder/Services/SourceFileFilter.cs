using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace FieldTakHub.Builder.Services
{
    public static class SourceFileFilter
    {
        private static readonly HashSet<string> Ign =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ".gitkeep", ".keep", ".placeholder",
            "placeholder.txt", "desktop.ini",
            "thumbs.db", ".ds_store"
        };
        public static bool IsValidDeploymentAsset(
            string filePath,
            string? categoryFolder = null
        )
        {
            if (string.IsNullOrWhiteSpace(filePath) ||
                !File.Exists(filePath)) return false;
            var info = new FileInfo(filePath);
            if (info.Length <= 0) return false;
            string n = info.Name;
            if (Ign.Contains(n)) return false;
            if (n.StartsWith(".", StringComparison.OrdinalIgnoreCase) ||
                n.IndexOf("placeholder", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return true;
        }
        public static string[] GetFilteredFiles(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        )
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !Directory.Exists(path)) return Array.Empty<string>();
            return Directory.GetFiles(path, searchPattern, searchOption)
                .Where(f => IsValidDeploymentAsset(f)).ToArray();
        }
        public static string[] GetFilteredFiles(
            string path,
            string searchPattern,
            EnumerationOptions options
        )
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !Directory.Exists(path)) return Array.Empty<string>();
            return Directory.GetFiles(path, searchPattern, options)
                .Where(f => IsValidDeploymentAsset(f)).ToArray();
        }
        public static IEnumerable<string> EnumerateFilteredFiles(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly
        )
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !Directory.Exists(path)) return Enumerable.Empty<string>();
            return Directory.EnumerateFiles(path, searchPattern, searchOption)
                .Where(f => IsValidDeploymentAsset(f));
        }
        public static IEnumerable<string> EnumerateFilteredFiles(
            string path,
            string searchPattern,
            EnumerationOptions options
        )
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !Directory.Exists(path)) return Enumerable.Empty<string>();
            return Directory.EnumerateFiles(path, searchPattern, options)
                .Where(f => IsValidDeploymentAsset(f));
        }
    }
}