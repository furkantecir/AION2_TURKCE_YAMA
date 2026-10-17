using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Interfaces;
using Aion2TR.Core.Models;
using Microsoft.Win32;

namespace Aion2TR.Infrastructure.Services;

public class WindowsSteamGameLocator : IGameLocator
{
    private static readonly string[] RequiredDirectories =
    [
        "Aion2",
        "Engine",
        Path.Combine("Aion2", "Content", "Paks")
    ];

    public Task<GameLocationResult> LocateGameAsync(CancellationToken cancellationToken = default)
    {
        // 1. Try Windows Steam Registry (HKCU / HKLM)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var registrySteamPath = GetSteamPathFromRegistry();
            if (!string.IsNullOrEmpty(registrySteamPath))
            {
                var candidates = FindGameInSteamLibraries(registrySteamPath);
                foreach (var path in candidates)
                {
                    if (ValidateGameDirectory(path))
                    {
                        return Task.FromResult(GameLocationResult.Found(path, "Steam Registry & LibraryFolders"));
                    }
                }
            }
        }

        // 2. Try common default drives (C, D, E, F)
        string[] commonDrives = ["C", "D", "E", "F"];
        foreach (var drive in commonDrives)
        {
            string[] paths =
            [
                $@"{drive}:\Program Files (x86)\Steam\steamapps\common\AION2",
                $@"{drive}:\Steam\steamapps\common\AION2",
                $@"{drive}:\SteamLibrary\steamapps\common\AION2",
                $@"{drive}:\uygulamalar\steam\steamapps\common\AION2",
                $@"{drive}:\Games\Steam\steamapps\common\AION2"
            ];

            foreach (var p in paths)
            {
                if (Directory.Exists(p) && ValidateGameDirectory(p))
                {
                    return Task.FromResult(GameLocationResult.Found(p, "Standard Drive Search"));
                }
            }
        }

        return Task.FromResult(GameLocationResult.NotFound("AION 2 kurulumu otomatik bulunamadı. Lütfen oyunu elle seçin."));
    }

    public bool ValidateGameDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        foreach (var req in RequiredDirectories)
        {
            var target = Path.Combine(path, req);
            if (!Directory.Exists(target))
                return false;
        }

        // Check for either Manifest_UFSFiles_Win64.txt or Aion2.exe in Binaries
        var manifestPath = Path.Combine(path, "Manifest_UFSFiles_Win64.txt");
        var exePath = Path.Combine(path, "Aion2", "Binaries", "Win64", "AION2.exe");

        return File.Exists(manifestPath) || File.Exists(exePath);
    }

    private static string? GetSteamPathFromRegistry()
    {
        try
        {
            // Try HKCU
            using var cuKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (cuKey?.GetValue("SteamPath") is string steamPath && !string.IsNullOrEmpty(steamPath))
                return steamPath.Replace('/', '\\');

            // Try HKLM
            using var lmKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam") ??
                              Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (lmKey?.GetValue("InstallPath") is string installPath && !string.IsNullOrEmpty(installPath))
                return installPath.Replace('/', '\\');
        }
        catch
        {
            // Registry read error, gracefully fallback
        }
        return null;
    }

    private static List<string> FindGameInSteamLibraries(string steamRoot)
    {
        var results = new List<string>();

        // 1. Direct steamapps in root
        var rootGamePath = Path.Combine(steamRoot, "steamapps", "common", "AION2");
        if (Directory.Exists(rootGamePath))
            results.Add(rootGamePath);

        // 2. Parse libraryfolders.vdf
        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                var content = File.ReadAllText(vdfPath);
                // "path"  "D:\\uygulamalar\\steam"
                var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                foreach (Match m in matches)
                {
                    var libPath = m.Groups[1].Value.Replace(@"\\", @"\");
                    var gamePath = Path.Combine(libPath, "steamapps", "common", "AION2");
                    if (Directory.Exists(gamePath))
                        results.Add(gamePath);
                }
            }
            catch
            {
                // Fallback to direct check
            }
        }

        return results;
    }
}
