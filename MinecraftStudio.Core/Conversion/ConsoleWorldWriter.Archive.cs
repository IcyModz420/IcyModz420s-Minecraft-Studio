// MinecraftStudio.Core/Conversion/ConsoleWorldWriter.Archive.cs: archive for ConsoleWorldWriter.
// Part of ConsoleWorldWriter (main part in ConsoleWorldWriter.cs).
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public partial class ConsoleWorldWriter
{
    // ---------------------------------------------------------------- archive, level.dat, wrapper

    public void Finish(WorldInfo info, IProgress<string>? progress = null)
    {
        progress?.Report("Building the console save archive");
        bool le = Platform.IsLittleEndian();
        var archive = new ConsoleArchive { LittleEndian = le, Version = ArchiveVersion, MinVersion = ArchiveVersion };
        long now = ConsoleArchive.Now1601Ms();
        // Every region of the square is listed (empty ones with size 0), Nether and End first like the game does.
        foreach (var dim in new[] { "minecraft:the_nether", "minecraft:the_end", "minecraft:overworld" })
        {
            string saved = _dim;
            _dim = dim;
            int half = DimSize / 2;
            int rMin = (-half) >> 5, rMax = (half - 1) >> 5;
            for (int rz = rMin; rz <= rMax; rz++)
                for (int rx = rMin; rx <= rMax; rx++)
                {
                    string name = RegionName(rx, rz);
                    var data = _regions.TryGetValue(name, out var r) && !r.IsEmpty ? r.ToBytes() : [];
                    archive.Files.Add(new ConsoleArchive.FileEntry { Name = name, Data = data, Modified = data.Length > 0 ? now : 0 });
                }
            _dim = saved;
        }
        foreach (var (name, data) in Maps.ConsoleEntries(info.Maps, _target))
            archive.Files.Add(new ConsoleArchive.FileEntry { Name = name, Data = data, Modified = now });
        if (_options.ConvertPlayers && info.Players.Count > 0 && HostPlayerFile(info, _options) is string playerFile)
        {
            archive.Files.Add(new ConsoleArchive.FileEntry { Name = playerFile, Data = NbtBinary.Serialize(HostPlayer(info), NbtFormat.Java), Modified = now });
            _notes.Add($"Your player was written as {playerFile}.");
        }
        var level = BuildLevel(info);
        archive.Files.Add(new ConsoleArchive.FileEntry { Name = "level.dat", Data = NbtBinary.Serialize(level, NbtFormat.Java), Modified = now });
        byte[] arc = archive.ToBytes();
        // SizeOnDisk = decompressed archive size.
        level.GetCompound("Data")!.SetLong("SizeOnDisk", arc.Length);
        archive.Files[^1].Data = NbtBinary.Serialize(level, NbtFormat.Java);
        arc = archive.ToBytes();
        progress?.Report("Compressing the console save");
        var (wrapper, fileName) = Platform switch
        {
            ConsolePlatform.Xbox360 => (ConsoleWrapper.X360Dat, "savegame.dat"),
            ConsolePlatform.PS3 => (ConsoleWrapper.Ps3Deflate, "GAMEDATA"),
            ConsolePlatform.WiiU => (ConsoleWrapper.ZlibBigEndian, "GAMEDATA"),
            ConsolePlatform.PSVita => (ConsoleWrapper.VitaRle, "GAMEDATA.bin"),
            _ => (ConsoleWrapper.ZlibLittleEndian, "GAMEDATA"),
        };
        byte[] wrapped = ConsoleWorld.Wrap(arc, wrapper);
        SafeFile.WriteAllBytes(Path.Combine(_output, fileName), wrapped);
        _notes.Add($"Wrote {_chunks:N0} chunks into a {_size}×{_size}-chunk {Platform.DisplayName()} world ({fileName}).");
        if (Platform == ConsolePlatform.Xbox360 && _options.ConsoleTemplatePackage is string template && File.Exists(template))
        {
            // Put the world into a copy of one of the user's own save packages (rebuilt and rehashed, not re-signed).
            var pkg = new StfsPackage(File.ReadAllBytes(template));
            var entry = pkg.Files().FirstOrDefault(f => f.Name.Equals("savegame.dat", StringComparison.OrdinalIgnoreCase))
                ?? throw new SaveFormatException("The template package does not contain savegame.dat.");
            byte[] package = pkg.Rebuild([("savegame.dat", wrapped, entry.UpdateTime, entry.AccessTime)]);
            SafeFile.WriteAllBytes(Path.Combine(_output, Path.GetFileName(template)), package);
            _notes.Add($"Xbox 360: the world was placed in a copy of {Path.GetFileName(template)} (package rebuilt and rehashed).");
        }
        else if (Platform == ConsolePlatform.Xbox360)
            _notes.Add("Xbox 360: choose one of your own saves as a template to get a ready package, or open your save in the editor and replace its savegame.dat.");
        else
            _notes.Add($"{Platform.DisplayName()}: the game data is written decrypted; import it with your platform's save tools.");
    }

    /// <summary>
    /// The archive name of the console owner's player file (players/&lt;id&gt;.dat): from the template save (the user's own) or
    /// a console source. The console only loads a player file named after its owner; without one the host's items go to
    /// the rescue chests at the spawn.
    /// </summary>
    public static string? HostPlayerFile(WorldInfo info, ConversionOptions options)
    {
        if (options.ConsoleTemplatePackage is string template && File.Exists(template))
        {
            try
            {
                using var w = LegacyConsole.ConsoleSave.Open(template);
                if (w.Players.FirstOrDefault()?.Source is string s) return s;
            }
            catch { }
        }
        return info.Players.FirstOrDefault()?.SourceFile is string f && (f.StartsWith("players/", StringComparison.OrdinalIgnoreCase) || f.StartsWith("players_", StringComparison.OrdinalIgnoreCase)) ? f : null;
    }

    /// <summary>The host in console coordinates (the world is centred on the spawn); outside the console world, at the spawn.</summary>
    NbtCompound HostPlayer(WorldInfo info)
    {
        var host = info.Players[0];
        var p = Players.ToJava(host, 0, _target, numericItemIds: true);
        var o = _offsetOverworld();
        int half = _size * 8 - 2;
        bool overworld = (host.Data.GetString("Dimension") ?? host.Dimension) == "minecraft:overworld";
        if (p.GetList("Pos") is NbtList { Count: 3 } pos)
        {
            double x = pos[0].AsDouble - o.X * 16, z = pos[2].AsDouble - o.Z * 16;
            if (overworld && Math.Abs(x) < half && Math.Abs(z) < half)
            {
                pos[0] = new NbtDouble(x);
                pos[1] = new NbtDouble(Math.Clamp(pos[1].AsDouble, 1, 255));
                pos[2] = new NbtDouble(z);
            }
            else
            {
                pos[0] = new NbtDouble(info.SpawnX - o.X * 16 + 0.5);
                pos[1] = new NbtDouble(Math.Clamp(info.SpawnY, 1, 250) + 1);
                pos[2] = new NbtDouble(info.SpawnZ - o.Z * 16 + 0.5);
                p.SetInt("Dimension", 0);
                ConversionLog.Note(LossKind.Player, "Your player", "placed at the spawn: where they stood is outside the console world");
            }
        }
        if (p.GetLongOrNull("SpawnX") is long sx)
        {
            long bx = sx - o.X * 16, bz = p.GetInt("SpawnZ") - o.Z * 16;
            if (Math.Abs(bx) < half && Math.Abs(bz) < half) { p.SetInt("SpawnX", (int)bx); p.SetInt("SpawnZ", (int)bz); }
            else
            {
                p.Remove("SpawnX"); p.Remove("SpawnY"); p.Remove("SpawnZ");
                ConversionLog.Note(LossKind.Player, "Your player", "bed spawn point not kept: it is outside the console world");
            }
        }
        return p;
    }

    NbtCompound BuildLevel(WorldInfo info)
    {
        var d = new NbtCompound();
        d.SetByte("MapFeatures", 1);
        d.SetInt("SpawnX", info.SpawnX - _offsetOverworld().X * 16);
        d.SetInt("SpawnY", Math.Clamp(info.SpawnY, 1, 250));
        d.SetInt("SpawnZ", info.SpawnZ - _offsetOverworld().Z * 16);
        d.SetLong("RandomSeed", info.Seed);
        d.SetString("LevelName", info.Name);
        d.SetString("generatorName", info.Generator == "flat" ? "flat" : "default");
        d.SetInt("generatorVersion", 1);
        d.SetInt("GameType", Math.Clamp(info.GameType, 0, 2));
        d.SetByte("spawnBonusChest", 0);
        d.SetByte("hasBeenInCreative", (sbyte)(info.GameType == 1 ? 1 : 0));
        d.SetLong("Time", info.Time);
        d.SetLong("SizeOnDisk", 0);
        d.SetLong("LastPlayed", ConsoleArchive.Now1601Ms());
        d.SetInt("version", 19132);
        d.SetInt("rainTime", info.RainTime);
        d.SetByte("raining", (sbyte)(info.Raining ? 1 : 0));
        d.SetByte("thundering", (sbyte)(info.Thundering ? 1 : 0));
        d.SetInt("thunderTime", info.ThunderTime);
        d.SetByte("hardcore", (sbyte)(info.Hardcore ? 1 : 0));
        d.SetByte("allowCommands", (sbyte)(info.AllowCommands ? 1 : 0));
        d.SetByte("initialized", 1);
        d.SetByte("newSeaLevel", 1);
        // Let the game place the stronghold itself.
        d.SetByte("hasStronghold", 0);
        d.SetInt("StrongholdX", 0); d.SetInt("StrongholdY", 0); d.SetInt("StrongholdZ", 0);
        d.SetByte("hasStrongholdEndPortal", 0);
        d.SetInt("StrongholdEndPortalX", 0); d.SetInt("StrongholdEndPortalZ", 0);
        d.SetInt("XZSize", _size);
        d.SetInt("HellScale", HellScale);
        return new NbtCompound { { "Data", d } };
    }

    (int X, int Z) _offsetOverworld() => OffsetFor("minecraft:overworld", _infoForArea ?? new WorldInfo());

    public void Dispose() { }
}
