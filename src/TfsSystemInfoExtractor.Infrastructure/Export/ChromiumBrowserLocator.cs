using System;
using System.IO;
using Microsoft.Win32;

namespace TfsSystemInfoExtractor.Infrastructure.Export
{
    /// <summary>
    /// Finds a locally installed Chromium-based browser (Edge, then Chrome) for headless
    /// print-to-PDF. Checks an explicit override, then the "App Paths" registry keys,
    /// then the standard install locations.
    /// </summary>
    internal static class ChromiumBrowserLocator
    {
        public static string? Locate(string? explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            {
                return explicitPath;
            }

            foreach (var exe in new[] { "msedge.exe", "chrome.exe" })
            {
                var fromRegistry = FromAppPaths(exe);
                if (fromRegistry != null)
                {
                    return fromRegistry;
                }
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new[]
            {
                Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
                Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe"),
                Combine(programFiles, @"Google\Chrome\Application\chrome.exe"),
                Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe"),
                Combine(localAppData, @"Google\Chrome\Application\chrome.exe")
            };

            foreach (var candidate in candidates)
            {
                if (candidate != null && File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string? FromAppPaths(string exeName)
        {
            const string subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = baseKey.OpenSubKey(subKey + exeName);
                    if (key?.GetValue(null) is string path && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch
                {
                    // registry access can be denied under some policies; fall through to path probing
                }
            }

            return null;
        }

        private static string? Combine(string? root, string relative) =>
            string.IsNullOrEmpty(root) ? null : Path.Combine(root, relative);
    }
}
