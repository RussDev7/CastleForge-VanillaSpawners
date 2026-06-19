/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2026 RussDev7
This file is part of https://github.com/RussDev7/CastleForge-VanillaSpawners - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace VanillaSpawners
{
    #region Runtime State

    /// <summary>
    /// Runtime switches used by the VanillaSpawners mod and Harmony patches.
    /// </summary>
    /// <remarks>
    /// Defaults preserve vanilla behavior until the mod config is loaded.
    /// </remarks>
    internal static class VanillaSpawnerRuntime
    {
        /// <summary>
        /// Master on/off switch for the VanillaSpawners mod.
        /// </summary>
        public static volatile bool Enabled = true;

        /// <summary>
        /// When false, newly generated terrain will not place vanilla spawner blocks.
        /// </summary>
        public static volatile bool GenerateSpawnerBlocks = true;

        /// <summary>
        /// When false, existing vanilla spawner blocks are treated as non-clickable by patched host-side code.
        /// </summary>
        public static volatile bool AllowSpawnerActivation = true;

        /// <summary>
        /// When false, newly generated terrain will not place vanilla LootBlock / LuckyLootBlock blocks.
        /// </summary>
        public static volatile bool GenerateLootBlocks = true;

        /// <summary>
        /// Logs blocked activation checks when activation is disabled.
        /// </summary>
        public static volatile bool LogBlockedActivation = false;
    }
    #endregion

    #region Load / Create / Apply

    /// <summary>
    /// Lightweight INI-backed config container for VanillaSpawners.
    /// </summary>
    internal sealed class VanillaSpawnersConfig
    {
        // Last successfully loaded config snapshot.
        internal static volatile VanillaSpawnersConfig Active;

        // [General].
        // Master toggle for the entire mod.
        public bool Enabled = true;

        // Allows new vanilla cave / alien / hell / boss spawner blocks to generate.
        public bool GenerateSpawnerBlocks = true;

        // Allows existing vanilla spawner blocks to be activated.
        public bool AllowSpawnerActivation = true;

        // Allows new vanilla LootBlock / LuckyLootBlock blocks to generate.
        public bool GenerateLootBlocks = true;

        // Logs each blocked spawner activation check.
        public bool LogBlockedActivation = false;

        /// <summary>
        /// Folder where the mod writes VanillaSpawners.Config.ini.
        /// </summary>
        public static string ConfigDir
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(VanillaSpawnersConfig).Namespace);
            }
        }

        /// <summary>
        /// Full path to VanillaSpawners.Config.ini.
        /// </summary>
        public static string ConfigPath
        {
            get
            {
                return Path.Combine(ConfigDir, "VanillaSpawners.Config.ini");
            }
        }

        /// <summary>
        /// Creates the config directory and default INI if missing,
        /// then parses the current file into a VanillaSpawnersConfig instance.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        public static VanillaSpawnersConfig LoadOrCreate()
        {
            // Ensure folder.
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Write default file once.
            if (!File.Exists(ConfigPath))
            {
                var lines = new[]
                {
                    $@"; VanillaSpawners - Configuration",
                    $@"; Lines starting with ';' or '#' are comments.",
                    $@"",
                    $@"[General]",
                    $@"; Master toggle for the entire mod.",
                    $@"; true  = VanillaSpawners patches use the options below.",
                    $@"; false = Vanilla behavior; this mod does not block vanilla spawner or loot generation.",
                    $@"Enabled=true",
                    $@"",
                    $@"; Allows new vanilla cave / alien / hell / boss spawner blocks to generate.",
                    $@"; false prevents NEW spawner blocks from being placed in newly generated terrain.",
                    $@"; Existing chunks and saves are not deleted or modified.",
                    $@"GenerateSpawnerBlocks=true",
                    $@"",
                    $@"; Allows existing vanilla spawner blocks to be activated.",
                    $@"; false makes existing vanilla spawner blocks non-clickable for the patched host/game instance.",
                    $@"AllowSpawnerActivation=true",
                    $@"",
                    $@"; Allows new vanilla LootBlock / LuckyLootBlock blocks to generate.",
                    $@"; false prevents NEW loot blocks from being placed in newly generated terrain.",
                    $@"; Existing chunks and saves are not deleted or modified.",
                    $@"GenerateLootBlocks=true",
                    $@"",
                    $@"; Logs blocked spawner activation checks.",
                    $@"; Useful for debugging, but noisy if players keep trying old spawners.",
                    $@"LogBlockedActivation=false",
                };

                // Write a default file.
                File.WriteAllLines(ConfigPath, lines);
            }

            // Parse INI -> object.
            var ini = SimpleIni.Load(ConfigPath);
            var c   = new VanillaSpawnersConfig();

            // [General].
            c.Enabled                = ini.GetBool("General", "Enabled",                c.Enabled);
            c.GenerateSpawnerBlocks  = ini.GetBool("General", "GenerateSpawnerBlocks",  c.GenerateSpawnerBlocks);
            c.AllowSpawnerActivation = ini.GetBool("General", "AllowSpawnerActivation", c.AllowSpawnerActivation);
            c.GenerateLootBlocks     = ini.GetBool("General", "GenerateLootBlocks",     c.GenerateLootBlocks);
            c.LogBlockedActivation   = ini.GetBool("General", "LogBlockedActivation",   c.LogBlockedActivation);

            return c;
        }

        /// <summary>
        /// Loads the config from disk, creates it when missing, validates values, and applies runtime statics.
        /// </summary>
        public static VanillaSpawnersConfig LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();

                cfg.ApplyToStatics();
                Active = cfg;

                Log($"Config: {ConfigPath}.");
                Log(
                    $"Enabled={cfg.Enabled}, "                               +
                    $"GenerateSpawnerBlocks={cfg.GenerateSpawnerBlocks}, "   +
                    $"AllowSpawnerActivation={cfg.AllowSpawnerActivation}, " +
                    $"GenerateLootBlocks={cfg.GenerateLootBlocks}, "         +
                    $"LogBlockedActivation={cfg.LogBlockedActivation}.");

                return cfg;
            }
            catch (Exception ex)
            {
                Log($"[VanillaSpawnersConfig] Failed to load/apply: {ex.Message}.");
                return Active;
            }
        }

        /// <summary>
        /// Applies file-backed config to the static runtime switches used by Harmony patches.
        /// </summary>
        private void ApplyToStatics()
        {
            VanillaSpawnerRuntime.Enabled = Enabled;

            // Disabled mod means vanilla behavior.
            VanillaSpawnerRuntime.GenerateSpawnerBlocks =
                !Enabled || GenerateSpawnerBlocks;

            VanillaSpawnerRuntime.AllowSpawnerActivation =
                !Enabled || AllowSpawnerActivation;

            VanillaSpawnerRuntime.GenerateLootBlocks =
                !Enabled || GenerateLootBlocks;

            VanillaSpawnerRuntime.LogBlockedActivation =
                Enabled && LogBlockedActivation;
        }
        #endregion
    }

    /// <summary>
    /// Tiny, case-insensitive INI reader.
    /// Supports [Section], key=value, ';' or '#' comments. No escaping, no multi-line.
    /// </summary>
    internal sealed class SimpleIni
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Loads an INI file from disk into a simple nested dictionary:
        ///   section -> (key -> value).
        /// Unknown / malformed lines are ignored.
        /// </summary>
        public static SimpleIni Load(string path)
        {
            var ini = new SimpleIni();
            string section = "";

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                // Section header: [SectionName].
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (!ini._data.ContainsKey(section))
                        ini._data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                // Key/value pair: key = value.
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (!ini._data.TryGetValue(section, out var dict))
                {
                    dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    ini._data[section] = dict;
                }
                dict[key] = val;
            }

            return ini;
        }

        /// <summary>
        /// Reads an int from the INI and clamps it to the inclusive range [min..max].
        /// Returns <paramref name="def"/> if missing/invalid before clamping.
        /// </summary>
        public int GetClamp(string sec, string key, int def, int min, int max)
        {
            var v = GetInt(sec, key, def);
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        /// <summary>
        /// Reads a string value from [section] key=... and returns <paramref name="def"/> if missing.
        /// </summary>
        public string GetString(string section, string key, string def)
            => (_data.TryGetValue(section, out var d) && d.TryGetValue(key, out var v)) ? v : def;

        /// <summary>
        /// Reads an int value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public int GetInt(string section, string key, int def)
            => int.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a double value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public double GetDouble(string section, string key, double def)
            => double.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)),
                               NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a bool value from [section] key=... and returns <paramref name="def"/> on failure.
        /// </summary>
        public bool GetBool(string section, string key, bool def)
        {
            var s = GetString(section, key, def ? "true" : "false");
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return def;
        }
    }
}