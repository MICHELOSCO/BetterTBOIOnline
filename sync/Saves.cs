using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BtoSync
{
	// What a rep+persistentgamedataN.dat says. Layout: "ISAACNGSAVE09R  " + 4 bytes, then chunks {int type, int size, int count, data}:
	// 1 achievements (1 byte each, index 0 unused), 2 event counters (int32, EventCounter enum: 1 Mom's Heart kills, 10 deaths),
	// 3 level counters (int32), 4 collectibles seen (1 byte each). Later chunks are not needed here.
	public class SaveInfo
	{
		public bool Valid { get; set; }
		public int Achievements { get; set; }
		public int AchievementTotal { get; set; }
		public int Deaths { get; set; }
		public int MomKills { get; set; }
		public int Items { get; set; }
		public int[] Counters { get; set; } = new int[0];  // all the event counters: completion marks live here
		public bool Played => Deaths + MomKills + Items > 0;
		public string Summary => !Valid ? "file non leggibile" : !Played ? "mai giocato" : $"{Achievements} obiettivi · {Deaths} morti · {Items} oggetti";

		public static SaveInfo Read(string file)
		{
			var i = new SaveInfo();
			try
			{
				var b = File.ReadAllBytes(file);
				if (b.Length < 0x20 || Encoding.ASCII.GetString(b, 0, 11) != "ISAACNGSAVE") return i;
				int o = 0x14;
				for (int chunk = 1; chunk <= 4; chunk++)
				{
					int type = BitConverter.ToInt32(b, o), count = BitConverter.ToInt32(b, o + 8), size = type == 2 || type == 3 ? 4 : 1;
					if (type != chunk || count < 0 || o + 12 + count * size > b.Length) return i;
					o += 12;
					if (type == 1) { i.AchievementTotal = count - 1; for (int k = 1; k < count; k++) if (b[o + k] != 0) i.Achievements++; }
					if (type == 2)
					{
						i.Counters = new int[count];
						for (int k = 0; k < count; k++) i.Counters[k] = BitConverter.ToInt32(b, o + 4 * k);
						if (count > 10) { i.MomKills = i.Counters[1]; i.Deaths = i.Counters[10]; }
					}
					if (type == 4) for (int k = 0; k < count; k++) if (b[o + k] != 0) i.Items++;
					o += count * size;
				}
				i.Valid = true;
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
			return i;
		}
	}

	public class SaveSlot
	{
		public int Slot { get; set; }
		public bool IsOpen { get; set; }  // its progress tree is shown
		public bool Exists { get; set; }
		public bool HasRun { get; set; }
		public DateTime Modified { get; set; }
		public SaveInfo Info { get; set; } = new SaveInfo();
		public string Title => "Slot " + Slot;
		public string AchText => Exists && Info.Valid ? $"{Info.Achievements} / {Info.AchievementTotal}" : "–";
		public double AchPct => Info.AchievementTotal > 0 ? 100.0 * Info.Achievements / Info.AchievementTotal : 0;
		bool Played => Info.Played || HasRun;
		public string Line1 => !Exists ? "Slot vuoto" : !Info.Valid ? "File non leggibile" : !Played ? "Mai giocato" : $"{Info.Deaths} morti · {Info.Items} oggetti scoperti";
		public string Line2 => Exists && Played ? $"Cuore di Mamma battuto {Info.MomKills} volte" : " ";
		public string Short => !Exists ? "vuoto" : !Played ? "mai giocato" : $"{Info.Achievements} obiettivi" + (HasRun ? ", partita in corso" : "");
		public string ModifiedText => Exists ? "Salvato il " + Modified.ToString("dd/MM/yyyy HH:mm") : " ";
	}

	// Completion marks: for each final boss, the event counter of each of the 34 characters (EventCounter enum,
	// PROGRESSION_KILL_<BOSS>_WITH_<CHARACTER>). 0 = not beaten, otherwise beaten; bit 2 = beaten on hard.
	public class BossProgress
	{
		public string Key, Name;
		public int[] Marks;  // one per character, in Progress.Characters order
		public int Beaten => Marks.Count(m => m != 0);
		public bool Hard => Marks.Any(Progress.IsHard);
		public int State => Hard ? 2 : Beaten > 0 ? 1 : 0;  // 0 not beaten, 1 beaten, 2 beaten on hard
	}

	public static class Progress
	{
		public static readonly string[] Characters =
		{
			"Isaac", "Maddalena", "Caino", "Giuda", "???", "Eva", "Sansone", "Azazel", "Lazzaro", "Eden", "The Lost", "Lilith", "Keeper", "Apollyon",
			"The Forgotten", "Bethany", "Jacob & Esau",
			"T. Isaac", "T. Maddalena", "T. Caino", "T. Giuda", "T. ???", "T. Eva", "T. Sansone", "T. Azazel", "T. Lazzaro", "T. Eden", "T. Lost",
			"T. Lilith", "T. Keeper", "T. Apollyon", "T. Forgotten", "T. Bethany", "T. Jacob",
		};

		public static bool IsHard(int mark) => (mark & 2) != 0;

		// The counters of one boss are split over the enum: the first 14 characters, then The Forgotten, Bethany,
		// Jacob & Esau and the 17 tainted ones were appended by later expansions.
		static int[] Split(int first14, int forgotten, int bethany, int jacob, int tainted) =>
			Enumerable.Range(first14, 14).Concat(new[] { forgotten, bethany, jacob }).Concat(Enumerable.Range(tainted, 17)).ToArray();

		static readonly (string key, string name, int[] idx)[] Bosses =
		{
			("heart", "Cuore di Mamma", Split(27, 203, 214, 215, 216)),
			("isaac", "Isaac", Split(41, 204, 233, 234, 235)),
			("satan", "Satana", Split(55, 205, 252, 253, 254)),
			("bluebaby", "???", Split(83, 207, 290, 291, 292)),
			("lamb", "Lamb", Split(97, 208, 309, 310, 311)),
			("megasatan", "Mega Satana", Split(116, 209, 328, 329, 330)),
			("bossrush", "Boss Rush", Split(69, 206, 271, 272, 273)),
			("hush", "Hush", Split(144, 211, 366, 367, 368)),
			("delirium", "Delirium", Split(173, 213, 404, 405, 406)),
			("mother", "Mother", Enumerable.Range(423, 34).ToArray()),
			("beast", "Beast", Enumerable.Range(457, 34).ToArray()),
			("greed", "Ultra Greed", Split(130, 210, 347, 348, 349)),
		};

		public static Dictionary<string, BossProgress> Of(SaveInfo info) => Bosses.ToDictionary(b => b.key, b => new BossProgress
		{
			Key = b.key, Name = b.name,
			Marks = b.idx.Select(i => i < info.Counters.Length ? info.Counters[i] : 0).ToArray(),
		});
	}

	public class SaveBackup
	{
		public string Path { get; set; }
		public int Slot { get; set; }
		public DateTime Date { get; set; }
		public bool FromGame { get; set; }  // the game's own daily copy in save_backups (progress only)
		public bool HasRun { get; set; }
		public string Label { get; set; }
		public SaveInfo Info { get; set; }
		public string Title => $"Slot {Slot} · {Date:dd/MM/yyyy HH:mm}";
		public string Sub => (FromGame ? "Copia automatica del gioco" : Label) + " · " + Info.Summary + (HasRun ? " · partita in corso" : "");
		public string Icon => FromGame ? "" : "";
		public System.Windows.Visibility DeleteVis => FromGame ? System.Windows.Visibility.Hidden : System.Windows.Visibility.Visible;  // Hidden keeps the buttons aligned
	}

	// The 3 save slots and their backups. A slot is: rep+persistentgamedataN.dat (progress), rep+gamestateN.dat (run in
	// progress, if any) and <game>\data\<mod>\saveN.dat (what each mod saved for that slot).
	public static class Saves
	{
		public static string SaveDir, DataDir;
		public static string BackupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BtoSync", "saves");
		public static string DocsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Binding of Isaac Repentance+");

		public static void Init(string modsDir)
		{
			DataDir = modsDir == null ? null : Path.Combine(Path.GetDirectoryName(modsDir), "data");
			SaveDir = FindSaveDir();
		}

		// Steam Cloud on (the default): Steam\userdata\<account>\250900\remote. Off: Documents\My Games.
		static string FindSaveDir()
		{
			var opts = Path.Combine(DocsDir, "options.ini");
			bool cloud = !(File.Exists(opts) && Regex.IsMatch(File.ReadAllText(opts), @"^SteamCloud=0", RegexOptions.Multiline));
			var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
			var users = steam == null ? null : Path.Combine(steam, "userdata");
			if (cloud && Directory.Exists(users))
			{
				// ponytail: several Steam accounts on one PC -> the one that saved last; add a picker if friends share PCs
				var best = Directory.GetDirectories(users).Select(u => Path.Combine(u, Core.AppId.ToString(), "remote"))
					.Where(d => Directory.Exists(d) && Directory.GetFiles(d, "rep+persistentgamedata?.dat").Length > 0)
					.OrderByDescending(d => Directory.GetFiles(d, "rep+persistentgamedata?.dat").Max(File.GetLastWriteTimeUtc))
					.FirstOrDefault();
				if (best != null) return best;
			}
			return DocsDir;
		}

		static string Persistent(int slot) => Path.Combine(SaveDir, $"rep+persistentgamedata{slot}.dat");
		static string GameState(int slot) => Path.Combine(SaveDir, $"rep+gamestate{slot}.dat");

		// mod folder name -> its save file for the slot
		static Dictionary<string, string> ModSaves(int slot) => DataDir == null || !Directory.Exists(DataDir) ? new Dictionary<string, string>()
			: Directory.GetDirectories(DataDir).Where(d => File.Exists(Path.Combine(d, $"save{slot}.dat")))
				.ToDictionary(Path.GetFileName, d => Path.Combine(d, $"save{slot}.dat"));

		public static List<SaveSlot> Slots() => Enumerable.Range(1, 3).Select(s =>
		{
			var f = Persistent(s);
			bool e = File.Exists(f);
			return new SaveSlot { Slot = s, Exists = e, Info = e ? SaveInfo.Read(f) : new SaveInfo(), HasRun = File.Exists(GameState(s)), Modified = e ? File.GetLastWriteTime(f) : default };
		}).ToList();

		// Backup folder: <yyyyMMdd-HHmmss-fff>-slotN\ persistentgamedata.dat, gamestate.dat, mods\<mod>\save.dat, label.txt
		public static string Backup(int slot, string label, string root = null)
		{
			if (!File.Exists(Persistent(slot))) return null;  // empty slot, nothing to keep
			var dir = Path.Combine(root ?? BackupDir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-slot{slot}");
			Directory.CreateDirectory(Path.Combine(dir, "mods"));
			File.Copy(Persistent(slot), Path.Combine(dir, "persistentgamedata.dat"));
			if (File.Exists(GameState(slot))) File.Copy(GameState(slot), Path.Combine(dir, "gamestate.dat"));
			foreach (var kv in ModSaves(slot))
			{
				Directory.CreateDirectory(Path.Combine(dir, "mods", kv.Key));
				File.Copy(kv.Value, Path.Combine(dir, "mods", kv.Key, "save.dat"));
			}
			File.WriteAllText(Path.Combine(dir, "label.txt"), label);
			return dir;
		}

		public const string AutoLabel = "Prima di Avvia Isaac";

		// Before the game starts: a backup of each slot changed since its last automatic one, then only the newest `keep` per slot stay.
		public static int AutoBackup(int keep = 5)
		{
			var auto = Backups().Where(b => !b.FromGame && b.Label == AutoLabel).ToList();
			int n = 0;
			foreach (var s in Slots().Where(x => x.Exists))
			{
				var last = auto.Where(b => b.Slot == s.Slot).Select(b => b.Date).DefaultIfEmpty().Max();
				if (s.Modified <= last) continue;  // not played since the last one
				if (Backup(s.Slot, AutoLabel) != null) n++;
			}
			foreach (var old in Prune(Backups().Where(b => !b.FromGame && b.Label == AutoLabel), keep)) Directory.Delete(old.Path, true);
			return n;
		}

		// the automatic backups beyond the newest `keep` of each slot
		public static IEnumerable<SaveBackup> Prune(IEnumerable<SaveBackup> auto, int keep) =>
			auto.GroupBy(b => b.Slot).SelectMany(g => g.OrderByDescending(b => b.Date).Skip(keep));

		public static List<SaveBackup> Backups()
		{
			var list = new List<SaveBackup>();
			if (Directory.Exists(BackupDir))
				foreach (var d in Directory.GetDirectories(BackupDir))
				{
					var m = Regex.Match(Path.GetFileName(d), @"^(\d{8}-\d{6})-\d{3}-slot([1-3])$");
					var p = Path.Combine(d, "persistentgamedata.dat");
					if (!m.Success || !File.Exists(p)) continue;
					var label = File.Exists(Path.Combine(d, "label.txt")) ? File.ReadAllText(Path.Combine(d, "label.txt")) : "Backup";
					list.Add(new SaveBackup
					{
						Path = d, Slot = int.Parse(m.Groups[2].Value), Date = DateTime.ParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", null),
						Info = SaveInfo.Read(p), HasRun = File.Exists(Path.Combine(d, "gamestate.dat")), Label = label,
					});
				}
			// the game's own daily copies, without the never-played slots
			var game = Path.Combine(DocsDir, "save_backups");
			if (Directory.Exists(game))
				foreach (var f in Directory.GetFiles(game, "*.rep+persistentgamedata?.dat"))
				{
					var m = Regex.Match(Path.GetFileName(f), @"^\d{8}\.rep\+persistentgamedata([1-3])\.dat$");
					var info = SaveInfo.Read(f);
					if (m.Success && info.Played)
						list.Add(new SaveBackup { Path = f, FromGame = true, Slot = int.Parse(m.Groups[1].Value), Date = File.GetLastWriteTime(f), Info = info });
				}
			return list.OrderByDescending(b => b.Date).ToList();
		}

		// The slot becomes exactly the backup; what it had before is backed up first.
		public static void Restore(SaveBackup b, int slot)
		{
			Backup(slot, $"Prima del ripristino ({b.Title})");
			if (b.FromGame) { Write(b.Path, null, null, slot); return; }  // no mod data in the game's copies: leave it as it is
			var mods = Path.Combine(b.Path, "mods");
			Write(Path.Combine(b.Path, "persistentgamedata.dat"), Path.Combine(b.Path, "gamestate.dat"),
				Directory.Exists(mods) ? Directory.GetDirectories(mods).Where(d => File.Exists(Path.Combine(d, "save.dat"))).ToDictionary(Path.GetFileName, d => Path.Combine(d, "save.dat")) : null,
				slot);
		}

		public static void Copy(int from, int to)
		{
			if (from == to || !File.Exists(Persistent(from))) return;
			Backup(to, $"Prima della copia dallo slot {from}");
			Write(Persistent(from), GameState(from), ModSaves(from), to);
		}

		static void Write(string persistent, string gamestate, Dictionary<string, string> mods, int slot)
		{
			File.Copy(persistent, Persistent(slot), true);
			// ponytail: with Steam Cloud on, Steam may download a deleted gamestate again (then the slot keeps its old run); not verified yet
			if (gamestate != null && File.Exists(gamestate)) File.Copy(gamestate, GameState(slot), true);
			else File.Delete(GameState(slot));
			if (mods == null || DataDir == null) return;
			foreach (var f in ModSaves(slot).Values) File.Delete(f);
			foreach (var kv in mods)
			{
				Directory.CreateDirectory(Path.Combine(DataDir, kv.Key));
				File.Copy(kv.Value, Path.Combine(DataDir, kv.Key, $"save{slot}.dat"), true);
			}
		}

		// Export: the slot as a zip with the same layout as a backup folder (+ slot.txt), to keep or send to someone.
		public static void Export(int slot, string zip)
		{
			var tmp = Path.Combine(Path.GetTempPath(), "bto-export-" + Guid.NewGuid().ToString("N"));
			try
			{
				var dir = Backup(slot, $"Slot {slot} esportato il {DateTime.Now:dd/MM/yyyy HH:mm}", tmp) ?? throw new InvalidOperationException($"lo slot {slot} è vuoto");
				File.WriteAllText(Path.Combine(dir, "slot.txt"), slot.ToString());
				File.Delete(zip);
				System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip);
			}
			finally { try { Directory.Delete(tmp, true); } catch { } }
		}

		// Import: an exported zip, or a bare persistentgamedata .dat, becomes a backup (restore it into a slot from there).
		// Nothing is written into the slots here. Returns the new backup's folder.
		public static string Import(string file)
		{
			var tmp = Path.Combine(Path.GetTempPath(), "bto-import-" + Guid.NewGuid().ToString("N"));
			try
			{
				Directory.CreateDirectory(tmp);
				if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) System.IO.Compression.ZipFile.ExtractToDirectory(file, tmp);
				else File.Copy(file, Path.Combine(tmp, "persistentgamedata.dat"));
				var p = Path.Combine(tmp, "persistentgamedata.dat");
				if (!File.Exists(p) || !SaveInfo.Read(p).Valid) throw new InvalidDataException("non è un salvataggio di Isaac Repentance+ (né uno esportato da BTO Sync)");
				int slot = File.Exists(Path.Combine(tmp, "slot.txt")) && int.TryParse(File.ReadAllText(Path.Combine(tmp, "slot.txt")).Trim(), out var s) && s >= 1 && s <= 3 ? s : 1;
				var old = File.Exists(Path.Combine(tmp, "label.txt")) ? File.ReadAllText(Path.Combine(tmp, "label.txt")) : "";
				File.Delete(Path.Combine(tmp, "slot.txt"));
				File.WriteAllText(Path.Combine(tmp, "label.txt"), $"Importato da {Path.GetFileName(file)}" + (old != "" ? $" ({old})" : ""));
				var dir = Path.Combine(BackupDir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-slot{slot}");
				Directory.CreateDirectory(BackupDir);
				CopyTree(tmp, dir);
				return dir;
			}
			finally { try { Directory.Delete(tmp, true); } catch { } }
		}

		static void CopyTree(string src, string dst)
		{
			Directory.CreateDirectory(dst);
			foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
			foreach (var d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
		}

		public static void Delete(SaveBackup b) { if (!b.FromGame) Directory.Delete(b.Path, true); }
	}
}
