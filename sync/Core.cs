using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Xml.Linq;
using Microsoft.Win32;

namespace BtoSync
{
	public class Notify : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;
		protected void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
		{
			field = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}
		protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}

	// One entry of the host code (lowercase names: JSON of the old BTO1 code). Since BTO2 the Workshop name and folder
	// are left out (name from Steam, see Titles) and the hash is cut to 8 characters (Core.SameHash); BTO3 drops the manifest too.
	public class HostMod
	{
		public string key { get; set; }
		public string folder { get; set; }
		public string name { get; set; }
		public string id { get; set; }
		public string version { get; set; }
		public string hash { get; set; }
		public string manifest { get; set; }
	}

	public class LocalMod : Notify
	{
		public string Key, Folder, Name, Id, Version, Hash, Manifest, Path, Description;
		public bool Link, Managed;  // Managed: a BTO fix that BTO Sync turns on and off by itself (Core.TiePlan)
		// Private: a private.txt in the folder marks a mod only for this PC: never in a host code, never touched by an alignment, never released
		public bool Private;
		public bool Bto => !Private && Core.IsBto(Folder);
		bool enabled, canUpdate, unlocked = true; string status = "", preview; Brush statusBrush = Theme.Muted;
		public string NameText => Name;
		public string VersionText => Version == "" ? "" : "v" + Version;
		public string Origin => Link ? "Sviluppo" : Id != "" ? "Workshop" : Private ? "Privata" : Bto ? "BTO" : "Locale";
		public Brush OriginBrush => Link ? Theme.Warn : Id != "" ? Theme.Blue : Bto ? Theme.Body : Theme.Muted;
		// shown instead of the preview image when there is none (Segoe MDL2: globe, code, sync, folder)
		public string OriginIcon => Id != "" ? "\uE774" : Link ? "\uE943" : Bto ? "\uE895" : "\uE8B7";
		public string DetailsText => $"Cartella {Folder}" + (Link ? " · collegata a una cartella di sviluppo" : !Managed ? ""
			: Core.Ties.TryGetValue(Folder, out var t) ? $" · si attiva e si disattiva da sola insieme a {t.name}" : " · resta sempre attiva e aggiornata");
		public string DescriptionText => Core.CleanDescription(Description) ?? "Questa mod non ha una descrizione nel suo metadata.xml.";
		public bool HasPage => Id != "";
		public bool CanRemove => !Link && Id != "";
		public bool Switchable => Unlocked && !Managed;
		public string Group => Managed ? "bto" : "";  // MainWindow groups the BTO fixes at the bottom of the list
		public string SwitchTip => Managed ? "BTO Sync la attiva e la disattiva da solo" : "Attiva o disattiva";
		public bool Enabled { get => enabled; set => Set(ref enabled, value); }
		public string Status { get => status; set => Set(ref status, value); }
		public Brush StatusBrush { get => statusBrush; set => Set(ref statusBrush, value); }
		public bool CanUpdate { get => canUpdate; set => Set(ref canUpdate, value); }
		public bool Unlocked { get => unlocked; set { if (unlocked != value) { Set(ref unlocked, value); Raise(nameof(Switchable)); } } }  // false while Isaac runs
		string requires = "";
		public string RequiresText { get => requires; set { Set(ref requires, value); Raise(nameof(HasRequires)); } }  // "Richiede: Custom Stage API"
		public bool HasRequires => requires != "";
		public string Preview { get => preview; set { Set(ref preview, value); Raise(nameof(HasPreview)); } }
		public bool HasPreview => !string.IsNullOrEmpty(preview);

		public void UpdateStatus(Dictionary<string, string> latest)
		{
			CanUpdate = false; StatusBrush = Theme.Muted;
			if (Id != "" && Core.Previews.TryGetValue(Id, out var pv)) Preview = pv;
			// only real states here: where the mod comes from is already in the badge
			if (Managed) Status = "Automatica";
			else if (Link || Id == "" || !latest.TryGetValue(Id, out var last)) Status = "";
			else if (Manifest != "" && Manifest != last) { Status = "Da aggiornare"; StatusBrush = Theme.Warn; CanUpdate = true; }
			else { Status = "Aggiornata"; StatusBrush = Theme.Ok; }
		}
	}

	// A saved set of active mods (r2modman-style profile); keys as in LocalMod.Key.
	public class Profile : Notify
	{
		string n; bool active, current;
		public string name { get => n; set => Set(ref n, value); }
		public List<string> keys { get; set; } = new List<string>();
		[ScriptIgnore] public string Summary => keys.Count == 1 ? "1 mod" : keys.Count + " mod";
		[ScriptIgnore] public bool IsActive { get => active; set { Set(ref active, value); Raise(nameof(ApplyText)); } }
		[ScriptIgnore] public bool IsCurrent { get => current; set => Set(ref current, value); }  // active, and the mods on now are exactly its list
		[ScriptIgnore] public string ApplyText => active ? "Riapplica" : "Applica";
	}

	public enum ActionKind { Enable, Disable, Download, Subscribe }

	public class SyncAction : Notify
	{
		public ActionKind Kind; public LocalMod Local; public HostMod Host;
		string state = "";
		public string Text { get; set; }
		public string KindIcon => Kind switch { ActionKind.Enable => "\uE73E", ActionKind.Disable => "\uE711", _ => "\uE896" };  // check, cross, download
		public Brush KindBrush => Kind switch { ActionKind.Enable => Theme.Ok, ActionKind.Disable => Theme.Muted, _ => Theme.Blue };
		public string State { get => state; set => Set(ref state, value); }
	}

	public class Plan
	{
		public List<SyncAction> Actions = new List<SyncAction>();
		public List<string> Notes = new List<string>();
		public void Add(ActionKind k, LocalMod l, HostMod h, string text) => Actions.Add(new SyncAction { Kind = k, Local = l, Host = h, Text = text });
	}

	public static class Theme
	{
		public static readonly Brush Muted = Freeze("#9BA3AA"), Ok = Freeze("#3FB97A"), Body = Freeze("#C9CED2"), Warn = Freeze("#E0A84B"), Bad = Freeze("#F0616D"),
			Blue = Freeze("#7FA8D9"), Red = Freeze("#F0616D");
		static Brush Freeze(string hex) { var b = (Brush)new BrushConverter().ConvertFrom(hex); b.Freeze(); return b; }
	}

	public static class Core
	{
		public const string Repo = "MICHELOSCO/BetterTBOIOnline";
		public const uint AppId = 250900;
		static readonly string[] TextExt = { ".lua", ".xml", ".txt", ".json", ".md", ".anm2", ".ini", ".csv" };
		internal static readonly HttpClient Http = MakeHttp();

		static HttpClient MakeHttp()
		{
			ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
			var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
			h.DefaultRequestHeaders.UserAgent.ParseAdd("bto-sync");
			return h;
		}

		public static bool Screenshots;  // --screenshots: the README's pictures, taken while the game may be open
		public static bool IsaacRunning => !Screenshots && Process.GetProcessesByName("isaac-ng").Length > 0;
		public static bool IsBto(string folder) => folder != null && (folder.StartsWith("bto-", StringComparison.OrdinalIgnoreCase) || folder.Equals("bettertboionline-diagnostics", StringComparison.OrdinalIgnoreCase));
		// "bto-join-sync" -> "BTO Join Sync": the name of a BTO mod from its folder (BTO2/BTO3 codes carry no names)
		public static string BtoName(string folder) => folder.Equals("bettertboionline-diagnostics", StringComparison.OrdinalIgnoreCase) ? "BetterTBOIOnline Diagnostics"
			: "BTO " + string.Join(" ", folder.Substring(4).Split('-').Where(w => w != "").Select(w => char.ToUpper(w[0]) + w.Substring(1)));
		// a host hash may be cut short (BTO2, BTO3): it matches when it is the start of the full one
		public static bool SameHash(string full, string host) => !string.IsNullOrEmpty(full) && !string.IsNullOrEmpty(host) && full.StartsWith(host, StringComparison.OrdinalIgnoreCase);
		// BTO fixes for a Workshop mod (folder -> Workshop id, name): active only while that mod is active.
		// Every other BTO mod in the releases fixes the base game and is always active.
		public static readonly Dictionary<string, (string id, string name)> Ties = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
		{
			["bto-astro-sync"] = ("3260980911", "Astrobirth"),
			["bto-faircoop-crash-fix"] = ("2678027825", "Fair Co-op Items"),
			["bto-goldenitems-sync"] = ("3338495603", "Golden Items"),
			["bto-sirens-fix"] = ("3650363740", "[Fish] Siren's Items Over Mom's"),
			["bto-stageapi-sync"] = ("1348031964", "StageAPI"),
			["bto-thefuture-sync"] = ("2993558907", "The Future"),
			["bto-thefuture-revive-fix"] = ("2993558907", "The Future"),
		};

		// Newest released version of each BTO mod, from the release assets "<folder>-<version>.zip".
		public static Dictionary<string, (string version, string url)> Catalog(Dictionary<string, string> assets)
		{
			var c = new Dictionary<string, (string version, string url)>(StringComparer.OrdinalIgnoreCase);
			foreach (var a in assets)
			{
				var m = Regex.Match(a.Key, @"^([a-z0-9-]+)-(\d+(?:\.\d+)+)\.zip$");  // lowercase: skips BTO-Sync-x.y.z.zip
				if (!m.Success || !IsBto(m.Groups[1].Value)) continue;
				var f = m.Groups[1].Value;
				if (!c.TryGetValue(f, out var o) || new Version(m.Groups[2].Value) > new Version(o.version)) c[f] = (m.Groups[2].Value, a.Value);
			}
			return c;
		}

		static bool Older(string mine, string release) => !Version.TryParse(mine, out var a) || !Version.TryParse(release, out var b) || a < b;

		// What the BTO mods must become given the Workshop mods: a tied fix follows its mod (installed with it,
		// disabled without it); an installed base-game fix stays active (a missing one is only proposed, see MissingBase);
		// all kept at the newest release.
		public static Plan TiePlan(List<LocalMod> local, Dictionary<string, (string version, string url)> catalog)
		{
			var p = new Plan();
			var on = new HashSet<string>(local.Where(m => m.Enabled && m.Id != "").Select(m => m.Id));
			foreach (var c in catalog)
			{
				var l = local.FirstOrDefault(m => m.Folder.Equals(c.Key, StringComparison.OrdinalIgnoreCase));
				if (l != null && l.Link) continue;
				bool tied = Ties.TryGetValue(c.Key, out var t), want = !tied || on.Contains(t.id);
				var name = l?.Name ?? c.Key;
				if (want && l == null && !tied) continue;  // base-game fix not installed: the user decides (MissingBase)
				if (want && c.Value.url != null && (l == null || Older(l.Version, c.Value.version)))
					p.Add(ActionKind.Download, l, new HostMod { key = c.Key, folder = c.Key, name = name, version = c.Value.version },
						$"Scarica {name} {c.Value.version}" + (tied ? $" (serve a {t.name})" : " (fix del gioco base)"));
				else if (want && l != null && !l.Enabled) p.Add(ActionKind.Enable, l, null, $"Attiva {name}");
				else if (!want && l != null && l.Enabled) p.Add(ActionKind.Disable, l, null, $"Disattiva {name} ({t.name} non è attiva)");
			}
			return p;
		}

		// Base-game fixes (bto-*, no Workshop mod behind them) that are released but not installed: proposed after the patch check.
		public static List<HostMod> MissingBase(List<LocalMod> local, Dictionary<string, (string version, string url)> catalog) =>
			catalog.Where(c => c.Value.url != null && c.Key.StartsWith("bto-", StringComparison.OrdinalIgnoreCase) && !Ties.ContainsKey(c.Key)
					&& !local.Any(m => m.Folder.Equals(c.Key, StringComparison.OrdinalIgnoreCase)))
				.Select(c => new HostMod { key = c.Key, folder = c.Key, name = c.Key, version = c.Value.version }).OrderBy(h => h.folder).ToList();

		static bool IsLink(string dir) => new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint);

		// ---- errors in words a player can act on; the system message goes to errori.log ----
		public static string ErrorLog => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsFile), "errori.log");
		static Exception Inner(Exception e) => e is AggregateException a && a.InnerException != null ? Inner(a.InnerException) : e;

		// null when there is nothing more useful to say than the message itself
		public static string ErrorHint(Exception e)
		{
			e = Inner(e);
			int code = e.HResult & 0xFFFF;  // Win32 error inside the HRESULT
			return e is HttpRequestException || e is WebException || e is TaskCanceledException || e is System.Net.Sockets.SocketException
					? "Non riesco a raggiungere Internet (GitHub o Steam): controlla la connessione e riprova."
				: e.Message.IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0 ? "Steam non è aperto o non è collegato: aprilo, accedi e riprova."
				: e is UnauthorizedAccessException ? "Windows non mi lascia scrivere lì: chiudi i programmi che usano quei file, oppure sposta BTO Sync in una cartella tua (non in Programmi)."
				: code == 0x70 || code == 0x27 ? "Il disco è pieno: libera un po' di spazio e riprova."
				: e is FileNotFoundException || e is DirectoryNotFoundException ? "Un file o una cartella non c'è più: premi Ricarica e riprova."
				: e is IOException ? "Un file è in uso da un altro programma (Isaac, Steam o una finestra di Esplora file): chiudilo e riprova."
				: null;
		}

		// One line for the messages inside the window: our own messages (plain Exception, already in Italian) as they are,
		// the common system errors as a hint, anything else as a generic line pointing to the log.
		public static string ErrorShort(Exception e)
		{
			e = Inner(e);
			return e.GetType() == typeof(Exception) ? e.Message
				: ErrorHint(e) ?? @"Qualcosa non ha funzionato: riprova. Il dettaglio è in %APPDATA%\BtoSync\errori.log.";
		}

		// ponytail: grows forever, one short line per error; trim it if it ever gets big
		public static void LogError(Exception e)
		{
			e = Inner(e);
			try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ErrorLog)); File.AppendAllText(ErrorLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {e.GetType().Name}: {e.Message}{Environment.NewLine}"); }
			catch { }
		}

		static string SettingsFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BtoSync", "modsdir.txt");
		public static void SaveModsDir(string dir) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile)); File.WriteAllText(SettingsFile, dir); }

		public static string FindModsDir()
		{
			try { var saved = File.ReadAllText(SettingsFile).Trim(); if (Directory.Exists(saved)) return saved; } catch { }
			var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
			if (steam == null) return null;
			var libs = new List<string> { steam };
			var vdf = System.IO.Path.Combine(steam, @"steamapps\libraryfolders.vdf");
			if (File.Exists(vdf))
				foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
					libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
			foreach (var lib in libs)
			{
				var d = System.IO.Path.Combine(lib, @"steamapps\common\The Binding of Isaac Rebirth\mods");
				if (Directory.Exists(d)) return System.IO.Path.GetFullPath(d);
			}
			return null;
		}

		static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-", "");

		// Short hash of a mod's files (paths + contents, disable.it and README.md excluded), equal on every PC with the same files.
		// Text files are hashed without CR: git autocrlf, zips and editors change line endings without changing the mod.
		public static string ModHash(string dir)
		{
			var root = System.IO.Path.GetFullPath(dir).TrimEnd('\\');
			var lines = new List<string>();
			using (var sha = SHA256.Create())
			{
				foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
				{
					var fn = System.IO.Path.GetFileName(f);
					if (fn.Equals("disable.it", StringComparison.OrdinalIgnoreCase) || fn.Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;  // not game content
					string h;
					if (TextExt.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
						h = Hex(sha.ComputeHash(File.ReadAllBytes(f).Where(x => x != 13).ToArray()));
					else using (var s = File.OpenRead(f)) h = Hex(sha.ComputeHash(s));
					lines.Add(f.Substring(root.Length + 1).Replace('\\', '/').ToLowerInvariant() + " " + h);
				}
				lines.Sort(StringComparer.Ordinal);
				return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)))).Substring(0, 16);
			}
		}

		// Workshop manifest per installed item, from Steam's appworkshop_250900.acf (same value as the API's hcontent_file).
		static Dictionary<string, string> InstalledManifests(string modsDir)
		{
			var t = new Dictionary<string, string>();
			try
			{
				var steamapps = Directory.GetParent(modsDir).Parent.Parent.FullName;
				var acf = System.IO.Path.Combine(steamapps, "workshop", $"appworkshop_{AppId}.acf");
				if (File.Exists(acf))
					foreach (Match m in Regex.Matches(File.ReadAllText(acf), "\"(\\d+)\"\\s*\\{[^{}]*?\"manifest\"\\s*\"(\\d+)\""))
						if (!t.ContainsKey(m.Groups[1].Value)) t[m.Groups[1].Value] = m.Groups[2].Value;
			}
			catch { }
			return t;
		}

		static (string name, string id, string version, string directory, string description) ReadMeta(string dir)
		{
			try
			{
				var x = XDocument.Load(System.IO.Path.Combine(dir, "metadata.xml")).Root;
				string V(string n) => (x.Element(n)?.Value ?? "").Trim();
				return (V("name"), V("id"), V("version"), V("directory"), V("description"));
			}
			catch { return ("", "", "", "", ""); }
		}

		// ponytail: hashes every mod on every scan (~1 s for 500 MB); cache by size+mtime if it gets slow.
		public static List<LocalMod> ScanMods(string modsDir)
		{
			var manifests = InstalledManifests(modsDir);
			var list = new List<LocalMod>();
			foreach (var d in Directory.GetDirectories(modsDir))
			{
				if (!File.Exists(System.IO.Path.Combine(d, "metadata.xml"))) continue;
				var meta = ReadMeta(d);
				var folder = System.IO.Path.GetFileName(d);
				bool priv = File.Exists(System.IO.Path.Combine(d, "private.txt"));
				list.Add(new LocalMod
				{
					Key = meta.id != "" ? meta.id : folder.ToLowerInvariant(),
					Folder = folder,
					Name = meta.name != "" ? meta.name : folder,
					Id = meta.id,
					Version = meta.version,
					Hash = ModHash(d),
					Manifest = meta.id != "" && manifests.TryGetValue(meta.id, out var mf) ? mf : "",
					Enabled = !File.Exists(System.IO.Path.Combine(d, "disable.it")),
					Link = IsLink(d),
					Private = priv,
					Managed = !priv && IsBto(folder) && !IsLink(d),
					Path = d,
					Description = meta.description,
				});
			}
			return list.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
		}

		public static void SetEnabled(LocalMod m, bool on)
		{
			if (m.Link) throw new InvalidOperationException("è un collegamento a una cartella di sviluppo: attivala o disattivala dal menu Mods del gioco");
			var f = System.IO.Path.Combine(m.Path, "disable.it");
			if (on) File.Delete(f); else File.WriteAllBytes(f, new byte[0]);
			m.Enabled = on;
		}

		class CodeDto { public int v { get; set; } public List<HostMod> mods { get; set; } }

		// BTO mods a BTO3 code names with one byte (2 + position). Append only: moving one breaks old codes.
		static readonly string[] KnownBto = { "bettertboionline-diagnostics", "bto-faircoop-crash-fix", "bto-join-sync", "bto-render-rng-guard",
			"bto-stageapi-sync", "bto-thefuture-revive-fix", "bto-thefuture-sync", "bto-treasure-jingle-sync", "bto-trinket-sync" };

		// BTO3: binary, base64 without padding. Per mod a first byte, then the first 4 bytes of its hash:
		//   0, varint Workshop id                        Workshop mod (name looked up on Steam by whoever reads it)
		//   2+i, version a.b.c in 3 bytes (c 255: a.b)   KnownBto[i]
		//   1, folder, version, name (varint length + UTF-8 each; name "" = BtoName)   anything else
		// No Workshop manifest: the hash already says whether two copies differ (MakePlan).
		// asRelease: folder -> (version, hash) to write instead of the local copy (a BTO mod linked to a dev folder
		// goes out as its GitHub release, which is what the others download).
		public static string ToCode(IEnumerable<LocalMod> mods, IDictionary<string, (string version, string hash)> asRelease = null)
		{
			var b = new List<byte>();
			void Varint(ulong n) { do { b.Add((byte)(n & 0x7F | (n > 0x7F ? 0x80u : 0))); n >>= 7; } while (n > 0); }
			void Text(string s) { var t = Encoding.UTF8.GetBytes(s ?? ""); Varint((ulong)t.Length); b.AddRange(t); }
			foreach (var m in mods.Where(m => !m.Private))
			{
				string ver = m.Version, hash = m.Hash ?? "";
				if (asRelease != null && asRelease.TryGetValue(m.Folder, out var r)) { ver = r.version; hash = r.hash ?? ""; }
				int k = Array.FindIndex(KnownBto, f => f.Equals(m.Folder, StringComparison.OrdinalIgnoreCase));
				var v = Regex.Match(ver ?? "", @"^(\d{1,3})\.(\d{1,3})(?:\.(\d{1,3}))?$");
				bool small = v.Success && v.Groups.Cast<Group>().Skip(1).All(g => !g.Success || int.Parse(g.Value) < 255);
				if (m.Id != "" && ulong.TryParse(m.Id, out var id)) { b.Add(0); Varint(id); }
				else if (k >= 0 && small)
				{
					b.Add((byte)(2 + k)); b.Add(byte.Parse(v.Groups[1].Value)); b.Add(byte.Parse(v.Groups[2].Value));
					b.Add(v.Groups[3].Success ? byte.Parse(v.Groups[3].Value) : (byte)255);
				}
				else { b.Add(1); Text(m.Folder); Text(ver); Text(IsBto(m.Folder) ? "" : m.Name); }
				for (int i = 0; i < 4; i++)
					b.Add(hash.Length >= 2 * i + 2 && byte.TryParse(hash.Substring(2 * i, 2), System.Globalization.NumberStyles.HexNumber, null, out var x) ? x : (byte)0);
			}
			return "BTO3:" + Convert.ToBase64String(b.ToArray()).TrimEnd('=');
		}

		public static List<HostMod> FromCode(string code)
		{
			code = Regex.Replace(code ?? "", @"[\s`]", "");  // Discord may wrap it in backticks
			if (!Regex.IsMatch(code, "^BTO[123]:")) throw new FormatException("Il codice deve iniziare con BTO3: (o BTO2:, BTO1:): fattelo ricopiare dall'host.");
			try
			{
				var b64 = code.Substring(5);
				var data = Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
				if (code.StartsWith("BTO3:")) return FromBto3(data);
				if (code.StartsWith("BTO1:"))
					using (var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
					using (var r = new StreamReader(gz, Encoding.UTF8))
						return new JavaScriptSerializer().Deserialize<CodeDto>(r.ReadToEnd()).mods ?? new List<HostMod>();
				string text;
				using (var z = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress))
				using (var r = new StreamReader(z, Encoding.UTF8)) text = r.ReadToEnd();
				var mods = new List<HostMod>();
				foreach (var line in text.Split('\n').Where(l => l.Length > 1))
				{
					var f = line.Substring(1).Split(new[] { '|' }, 4);
					if (line[0] == 'w') mods.Add(new HostMod { key = f[0], id = f[0], manifest = f[1], hash = f[2], version = "" });
					else if (line[0] == 'b') mods.Add(new HostMod { key = f[0].ToLowerInvariant(), folder = f[0], name = BtoName(f[0]), id = "", version = f[1], hash = f[2] });
					else if (line[0] == 'l') mods.Add(new HostMod { key = f[0].ToLowerInvariant(), folder = f[0], name = f[3], id = "", version = f[1], hash = f[2] });
					else throw new InvalidDataException(line);
				}
				return mods;
			}
			catch (Exception e) when (!(e is FormatException && e.Message.StartsWith("Il codice")))
			{ throw new FormatException("Codice incompleto o rovinato: copialo di nuovo tutto, dall'inizio (BTO3:) alla fine."); }
		}

		static List<HostMod> FromBto3(byte[] d)
		{
			int p = 0;
			byte Next() => p < d.Length ? d[p++] : throw new InvalidDataException("short");
			ulong Varint() { ulong n = 0; for (int s = 0; ; s += 7) { byte x = Next(); n |= (ulong)(x & 0x7F) << s; if (x < 0x80 || s > 56) return n; } }
			string Text() { int n = (int)Varint(); if (n > d.Length - p) throw new InvalidDataException("short"); var s = Encoding.UTF8.GetString(d, p, n); p += n; return s; }
			var mods = new List<HostMod>();
			while (p < d.Length)
			{
				byte t = Next(); HostMod h;
				if (t == 0) { var id = Varint().ToString(); h = new HostMod { key = id, id = id, version = "" }; }
				else if (t == 1) { var f = Text(); var v = Text(); var n = Text(); h = new HostMod { key = f.ToLowerInvariant(), folder = f, id = "", version = v, name = n != "" ? n : IsBto(f) ? BtoName(f) : f }; }
				else
				{
					if (t - 2 >= KnownBto.Length) throw new FormatException("Il codice viene da un BTO Sync più nuovo: aggiorna il programma (Impostazioni > Aggiorna).");
					var f = KnownBto[t - 2];
					byte a = Next(), b2 = Next(), c = Next();
					h = new HostMod { key = f, folder = f, id = "", name = BtoName(f), version = c == 255 ? $"{a}.{b2}" : $"{a}.{b2}.{c}" };
				}
				h.hash = string.Concat(Enumerable.Range(0, 4).Select(_ => Next().ToString("x2")));
				mods.Add(h);
			}
			return mods;
		}

		internal static System.Collections.IEnumerable Arr(object o) => o as System.Collections.IEnumerable ?? new object[0];

		public static readonly Dictionary<string, string> Previews = new Dictionary<string, string>();  // Workshop id -> preview image url
		public static readonly Dictionary<string, string> Titles = new Dictionary<string, string>();  // Workshop id -> title

		// BTO2/BTO3 codes carry no Workshop names: from Steam (LatestManifests), else the local copy, else the id.
		public static void FillNames(List<HostMod> host, List<LocalMod> local)
		{
			foreach (var h in host.Where(h => string.IsNullOrEmpty(h.name)))
				h.name = Titles.TryGetValue(h.id ?? "", out var t) ? t : local.FirstOrDefault(l => l.Key == h.key)?.Name ?? "mod del Workshop " + h.id;
		}

		// Hash of a BTO release zip ("<folder>/..." inside), as ModHash computes it on the installed folder. Cached per url.
		static readonly Dictionary<string, string> releaseHashes = new Dictionary<string, string>();
		public static async Task<string> ReleaseHash(string url, string folder)
		{
			if (releaseHashes.TryGetValue(url, out var known)) return known;
			var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bto-release-" + Guid.NewGuid().ToString("N"));
			try
			{
				Directory.CreateDirectory(tmp);
				var zip = System.IO.Path.Combine(tmp, "mod.zip");
				File.WriteAllBytes(zip, await Http.GetByteArrayAsync(url));
				ZipFile.ExtractToDirectory(zip, System.IO.Path.Combine(tmp, "x"));
				return releaseHashes[url] = ModHash(System.IO.Path.Combine(tmp, "x", folder));
			}
			finally { try { Directory.Delete(tmp, true); } catch { } }
		}

		// Latest Workshop manifest (hcontent_file) per item, from the keyless Steam Web API (also fills Previews).
		public static async Task<Dictionary<string, string>> LatestManifests(ICollection<string> ids)
		{
			var r = new Dictionary<string, string>();
			if (ids.Count == 0) return r;
			var form = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("itemcount", ids.Count.ToString()) };
			int i = 0;
			foreach (var id in ids) form.Add(new KeyValuePair<string, string>($"publishedfileids[{i++}]", id));
			var resp = await Http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", new FormUrlEncodedContent(form));
			var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(await resp.Content.ReadAsStringAsync());
			foreach (Dictionary<string, object> d in Arr(((Dictionary<string, object>)root["response"])["publishedfiledetails"]))
				{
				if (d.TryGetValue("hcontent_file", out var h)) r[d["publishedfileid"].ToString()] = h.ToString();
				if (d.TryGetValue("preview_url", out var pv)) Previews[d["publishedfileid"].ToString()] = pv.ToString();
				if (d.TryGetValue("title", out var ti)) Titles[d["publishedfileid"].ToString()] = ti.ToString();
			}
			return r;
		}

		// Release assets of the repo, newest release first: name -> url ("<folder>-<version>.zip").
		public static async Task<Dictionary<string, string>> BtoAssets()
		{
			var a = new Dictionary<string, string>();
			var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases?per_page=100");
			foreach (Dictionary<string, object> rel in Arr(new JavaScriptSerializer().DeserializeObject(json)))
				foreach (Dictionary<string, object> x in Arr(rel["assets"]))
					if (!a.ContainsKey((string)x["name"])) a[(string)x["name"]] = (string)x["browser_download_url"];
			return a;
		}

		// Staging folder on the same volume as mods/ (Directory.Move cannot cross drives).
		static string Staging(string modsDir)
		{
			var d = System.IO.Path.Combine(Directory.GetParent(modsDir).FullName, ".bto-sync-tmp", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(d);
			return d;
		}

		static void DeleteFolder(string dir)
		{
			if (!Directory.Exists(dir)) return;
			if (IsLink(dir)) throw new InvalidOperationException(System.IO.Path.GetFileName(dir) + " è un collegamento: non la tocco");
			Directory.Delete(dir, true);
		}

		static void ReplaceFolder(string src, string dst) { DeleteFolder(dst); Directory.Move(src, dst); }

		// Returns a warning when the release differs from the host's copy.
		public static async Task<string> InstallBto(HostMod h, string url, string modsDir)
		{
			var tmp = Staging(modsDir);
			try
			{
				var zip = System.IO.Path.Combine(tmp, "mod.zip");
				File.WriteAllBytes(zip, await Http.GetByteArrayAsync(url));
				ZipFile.ExtractToDirectory(zip, System.IO.Path.Combine(tmp, "x"));
				var src = System.IO.Path.Combine(tmp, "x", h.folder);
				if (!File.Exists(System.IO.Path.Combine(src, "metadata.xml"))) throw new InvalidDataException("lo zip non contiene la cartella " + h.folder);
				var dst = System.IO.Path.Combine(modsDir, h.folder);
				ReplaceFolder(src, dst);
				return string.IsNullOrEmpty(h.hash) || SameHash(ModHash(dst), h.hash) ? null : $"la copia dell'host è diversa dalla release {h.version}: chiedigli la cartella";
			}
			finally { try { Directory.Delete(tmp, true); } catch { } }
		}

		// Copies a downloaded Workshop item into mods/ exactly as Isaac does at startup: "<metadata directory>_<id>".
		public static void InstallWorkshopCopy(string src, string id, string modsDir)
		{
			var dir = ReadMeta(src).directory;
			var target = System.IO.Path.Combine(modsDir, (dir != "" ? dir : id) + "_" + id);
			bool wasDisabled = false;
			var tmp = Staging(modsDir);
			try
			{
				var staged = System.IO.Path.Combine(tmp, "m");
				CopyDir(src, staged);
				foreach (var old in Directory.GetDirectories(modsDir, "*_" + id))
				{
					wasDisabled |= File.Exists(System.IO.Path.Combine(old, "disable.it"));
					if (!old.Equals(target, StringComparison.OrdinalIgnoreCase)) DeleteFolder(old);
				}
				ReplaceFolder(staged, target);
				if (wasDisabled) File.WriteAllBytes(System.IO.Path.Combine(target, "disable.it"), new byte[0]);
			}
			finally { try { Directory.Delete(tmp, true); } catch { } }
		}

		public static void RemoveWorkshopCopy(string id, string modsDir)
		{
			foreach (var old in Directory.GetDirectories(modsDir, "*_" + id)) DeleteFolder(old);
		}

		public static bool HasWorkshopCopy(string id, string modsDir) => modsDir != null && Directory.GetDirectories(modsDir, "*_" + id).Length > 0;

		static void CopyDir(string src, string dst)
		{
			Directory.CreateDirectory(dst);
			foreach (var f in Directory.GetFiles(src)) File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)));
			foreach (var d in Directory.GetDirectories(src)) CopyDir(d, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(d)));
		}

		// ---- profiles: %APPDATA%\BtoSync\profiles.json ----
		class ProfileFile { public string active { get; set; } public List<Profile> profiles { get; set; } }
		static string ProfilesFile => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsFile), "profiles.json");

		public static (List<Profile> list, string active) LoadProfiles()
		{
			try
			{
				var f = new JavaScriptSerializer().Deserialize<ProfileFile>(File.ReadAllText(ProfilesFile));
				return (f.profiles ?? new List<Profile>(), f.active);
			}
			catch { return (new List<Profile>(), null); }
		}

		public static void SaveProfiles(IEnumerable<Profile> list, string active)
		{
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ProfilesFile));
			File.WriteAllText(ProfilesFile, new JavaScriptSerializer().Serialize(new ProfileFile { active = active, profiles = list.ToList() }));
		}

		// Enables exactly the profile's mods; returns the names of the profile's mods that are not installed.
		public static List<string> ApplyProfile(Profile p, List<LocalMod> local)
		{
			var want = new HashSet<string>(p.keys);
			foreach (var m in local.Where(m => !m.Link && m.Enabled != want.Contains(m.Key))) SetEnabled(m, want.Contains(m.Key));
			return p.keys.Where(k => !local.Any(m => m.Key == k)).ToList();
		}

		// The "online mods" patch of Isaac-Online-Modded (github.com/xADDBx/Isaac-Online-Modded) on isaac-ng.exe: the game's
		// "mods banned online" check (3 je after sub eax,2 / sub eax,1 / sub eax,1) becomes nops. Byte pattern, not an address,
		// as that tool does: Present/Absent only when exactly one copy is found, else Unknown (a game update changed the code).
		public enum PatchState { NoExe, Absent, Present, Unknown }
		static readonly byte[] PatchedCheck = { 0x83, 0xE8, 0x02, 0x90, 0x90, 0x83, 0xE8, 0x01, 0x90, 0x90, 0x83, 0xE8, 0x01, 0x90, 0x90, 0x32, 0xC0 };
		public const string PatcherRepo = "xADDBx/Isaac-Online-Modded";

		public static PatchState OnlinePatch(string exe)
		{
			if (!File.Exists(exe)) return PatchState.NoExe;
			var d = File.ReadAllBytes(exe);
			int done = 0, orig = 0, n = PatchedCheck.Length;
			for (int i = 0; i + n <= d.Length; i++)
			{
				if (d[i] != 0x83) continue;
				bool p = true, o = true;
				for (int k = 0; k < n && (p || o); k++)
				{
					p &= d[i + k] == PatchedCheck[k];
					// unpatched: "74 xx" (je rel8) where the patch has "90 90"
					o &= PatchedCheck[k] != 0x90 ? d[i + k] == PatchedCheck[k] : k % 5 == 4 || d[i + k] == 0x74;
				}
				if (p) done++; else if (o) orig++;
			}
			return done == 1 && orig == 0 ? PatchState.Present : orig == 1 && done == 0 ? PatchState.Absent : PatchState.Unknown;
		}

		// Downloads the patcher (IsaacOnlineModded.exe of its latest release) next to BTO Sync's settings; returns its path.
		public static async Task<string> DownloadPatcher()
		{
			var rel = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(await Http.GetStringAsync($"https://api.github.com/repos/{PatcherRepo}/releases/latest"));
			var url = Arr(rel["assets"]).Cast<Dictionary<string, object>>().Where(x => (string)x["name"] == "IsaacOnlineModded.exe").Select(x => (string)x["browser_download_url"]).FirstOrDefault()
				?? throw new InvalidDataException("nell'ultima versione di Isaac-Online-Modded non c'è IsaacOnlineModded.exe: scaricalo a mano dalla sua pagina GitHub");
			var dst = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsFile), "IsaacOnlineModded.exe");
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst));
			File.WriteAllBytes(dst, await Http.GetByteArrayAsync(url));
			return dst;
		}

		// ---- REPENTOGON (repentogon.com): its launcher builds a patched copy of the game in <game>\Repentogon and starts it;
		// the mods stay in <game>\mods. Avvia Isaac goes through the launcher when there is one (MainWindow.Launch_Click). ----
		static string BtoDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BtoSync");
		public static string RepentogonChoice => Path.Combine(BtoDir, "repentogon.txt");  // the launcher picked by hand in Impostazioni
		public static string RepentogonOffFile => Path.Combine(BtoDir, "repentogon-off.txt");  // present: Avvia Isaac ignores REPENTOGON
		static string GameDir(string modsDir) => modsDir == null ? null : Path.GetDirectoryName(modsDir.TrimEnd('\\', '/'));
		public static string IsaacExe(string modsDir) => GameDir(modsDir) == null ? null : Path.Combine(GameDir(modsDir), "isaac-ng.exe");

		// the copy the launcher made: REPENTOGON is installed even when its launcher is somewhere we do not look
		public static bool RepentogonCopy(string modsDir) => GameDir(modsDir) != null && File.Exists(Path.Combine(GameDir(modsDir), "Repentogon", "zhlRepentogon.dll"));

		// REPENTOGONLauncher.exe: the one picked by hand, the places repentogon.com suggests (a folder in the game folder or at a
		// drive's root), or the one in Isaac's Steam launch options ("<path>\REPENTOGONLauncher.exe" --isaac=%command%).
		public static string RepentogonLauncher(string modsDir)
		{
			try { var s = File.ReadAllText(RepentogonChoice).Trim(); if (File.Exists(s)) return s; } catch { }
			var tries = new List<string>();
			if (GameDir(modsDir) != null) tries.Add(Path.Combine(GameDir(modsDir), "REPENTOGONLauncher", "REPENTOGONLauncher.exe"));
			try { tries.AddRange(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => Path.Combine(d.Name, "REPENTOGONLauncher", "REPENTOGONLauncher.exe"))); } catch { }
			var found = tries.FirstOrDefault(File.Exists);
			if (found != null) return found;
			try
			{
				var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
				foreach (var vdf in Directory.GetFiles(Path.Combine(steam, "userdata"), "localconfig.vdf", SearchOption.AllDirectories))
					foreach (Match m in Regex.Matches(File.ReadAllText(vdf), @"[A-Za-z]:(?:\\{1,2}[^""\\]+)*?\\{1,2}REPENTOGONLauncher\.exe", RegexOptions.IgnoreCase))
					{
						var path = m.Value.Replace(@"\\", @"\");
						if (File.Exists(path)) return path;
					}
			}
			catch { }
			return null;
		}

		// A Workshop mod that needs REPENTOGON, from what its author wrote: a tag in the title ("[REPENTOGON]", "(RGON)") or a
		// sentence about the mod ("Requires REPENTOGON", "REPENTOGON is required"). "Not required", "works without", "X or
		// REPENTOGON" and "the details require REPENTOGON" do not count. ponytail: wording rules, tuned on 185 Workshop mods.
		const string Rg = @"repentogon|rgon\b";
		// a title naming it ("[REPENTOGON]", "More Jingles! REPENTOGON") means made for it, unless it says no/compatible/optional;
		// REPENTOGON's own Workshop page ("REPENTOGON: Isaac Script Extender") is not a mod that needs it
		static readonly Regex RgTitleNo = new Regex(@"\b(no|non|without|w/o)[\s-]+(" + Rg + @")|(" + Rg + @")\W+(\w+\W+){0,1}(compat\w*|support\w*|optional|friendly)|(compat\w*|support\w*|optional)\W+(\w+\W+){0,2}(" + Rg + @")|script\s+extender", RegexOptions.IgnoreCase);
		static readonly Regex RgTitleYes = new Regex(@"\b(" + Rg + ")", RegexOptions.IgnoreCase);
		static readonly Regex RgNo = new Regex(@"\b(not|n't|no|never|without)\W+(\w+\W+){0,2}(requir|need)\w*\W+(\w+\W+){0,3}(" + Rg + @")|(" + Rg + @")\W+(\w+\W+){0,2}(is\s+|are\s+)?(not|n't)\s+(\w+\s+)?(required|needed|necessary|mandatory)|\b(no|without)\s+(" + Rg + @")\b", RegexOptions.IgnoreCase);
		static readonly Regex RgYes = new Regex(@"(?:^|[.!?•|\n\-:*>]\s*|\b(?:this\s+mod|the\s+mod|mod|it|this)\s+)(?:requires?|needs?|depends\s+on|must\s+(?:have|use)|only\s+works\s+with)[\s:!]+(?:the\s+)?(?:" + Rg + @")(?!\s+or\b)" +
			@"|(?:requirements?|dependenc(?:y|ies)|requires)\s*:\s*[\w\s,&+()-]{0,40}?\b(?:" + Rg + @")(?!\s+or\b)" +
			@"|(?<!\bor\s)\b(?:" + Rg + @")\s+(?:is\s+|are\s+)?(?:required|needed|mandatory|necessary)\b" +
			@"|(?:won.t|doesn.t|does\s+not|will\s+not|can.t|cannot)\s+(?:work|run|function)\s+without\s+(?:the\s+)?(?:" + Rg + ")", RegexOptions.IgnoreCase | RegexOptions.Multiline);

		public static bool RequiresRepentogon(string title, string description)
		{
			title = title ?? "";
			if (RgTitleNo.IsMatch(title)) return false;
			if (RgTitleYes.IsMatch(title)) return true;
			var t = title + "\n" + (description ?? "");
			return !RgNo.IsMatch(t) && RgYes.IsMatch(t);
		}

		// Steam "Required items" of a Workshop mod (e.g. The Future -> Custom Stage API). The Steamworks API of
		// Facepunch 2.3.3 does not expose them and the keyless Web API has none, so they are read from the item's
		// public page, once per session. ponytail: HTML scraping; if Steam changes the page this returns nothing.
		static readonly Dictionary<string, List<(string id, string name)>> required = new Dictionary<string, List<(string id, string name)>>();

		public static List<(string id, string name)> ParseRequired(string html) =>
			Regex.Matches(html ?? "", @"filedetails/\?id=(\d+)""[^>]*>\s*<div class=""requiredItem"">\s*([^<]*?)\s*</div>")
				.Cast<Match>().Select(m => (m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value).Trim())).Distinct().ToList();

		public static async Task<List<(string id, string name)>> RequiredItems(string id)
		{
			lock (required) if (required.TryGetValue(id, out var r)) return r;
			var list = ParseRequired(await Http.GetStringAsync($"https://steamcommunity.com/sharedfiles/filedetails/?id={id}&l=english"));
			lock (required) required[id] = list;
			return list;
		}

		public static List<(string id, string name)> KnownRequired(string id) { lock (required) return required.TryGetValue(id, out var r) ? r : null; }

		// An active mod whose required mod is missing (Req == null) or disabled.
		public class DepIssue { public LocalMod Mod, Req; public string Id, Name; }

		public static List<DepIssue> DepIssues(List<LocalMod> local) =>
			local.Where(m => m.Enabled && m.Id != "").SelectMany(m => (KnownRequired(m.Id) ?? new List<(string id, string name)>())
					.Select(r => new DepIssue { Mod = m, Id = r.id, Name = r.name, Req = local.FirstOrDefault(x => x.Id == r.id) }))
				.Where(d => d.Req == null || !d.Req.Enabled).ToList();

		// For tests: what RequiredItems would have read from Steam.
		internal static void SetRequired(string id, List<(string id, string name)> r) { lock (required) required[id] = r; }

		// Uninstall: Workshop copies only (BTO mods follow them, see TiePlan); never a junction.
		public static void DeleteMod(LocalMod m)
		{
			if (!m.CanRemove) throw new InvalidOperationException("questa mod non si può disinstallare da qui: non è del Workshop");
			DeleteFolder(m.Path);
		}

		// Workshop descriptions are BBCode: keep the text, drop images and markup.
		public static string CleanDescription(string s)
		{
			if (string.IsNullOrWhiteSpace(s)) return null;
			const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.Singleline;
			s = s.Replace("\r", "");
			s = Regex.Replace(s, @"\[(img|previewyoutube)[^\]]*\].*?\[/\1\]", "", O);
			s = Regex.Replace(s, @"\[url=[^\]]*\](.*?)\[/url\]", "$1", O);
			s = Regex.Replace(s, @"\[\*\]\s*", "• ", O);
			s = Regex.Replace(s, @"\[/?(h[1-6]|b|i|u|s|strike|spoiler|noparse|list|olist|quote|code|table|tr|td|th|hr|url)(=[^\]]*)?\]", "", O);
			s = Regex.Replace(s, @"[ \t]+\n", "\n");
			s = Regex.Replace(s, @"\n{3,}", "\n\n");
			return s.Trim();
		}

		// The game's log.txt: desyncs and Lua errors per mod folder ("" = the game itself). The log also names the players
		// and their Steam IDs: only these counts ever leave this function.
		public class GameLog { public int Desyncs, FirstDesyncFrame = -1; public Dictionary<string, int> LuaErrors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); }
		public static string GameLogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Binding of Isaac Repentance+", "log.txt");

		public static GameLog ReadGameLog(string text)
		{
			var g = new GameLog();
			var seen = new HashSet<string>();
			foreach (var line in text.Split('\n'))
			{
				var d = Regex.Match(line, @"Desync detected on frame: (\d+)");
				if (d.Success) { if (g.Desyncs++ == 0) g.FirstDesyncFrame = int.Parse(d.Groups[1].Value); continue; }
				if (!Regex.IsMatch(line, @"\.lua:\d+:") || !seen.Add(line.Trim())) continue;  // the same error every frame counts once
				var m = Regex.Match(line, @"mods[/\\]([^/\\]+)[/\\]");
				var key = m.Success ? m.Groups[1].Value : "";
				g.LuaErrors[key] = g.LuaErrors.TryGetValue(key, out var n) ? n + 1 : 1;
			}
			return g;
		}

		// ---- log upload (opt-in): the last game of log.txt, cleaned on this PC, to the Worker in logs-worker/ ----
		public const string LogsUrl = "https://bto-logs.bto-logs-worker.workers.dev/";  // the Worker (logs-worker/README.md); empty = no upload
		public static string LogsUrlNow => Environment.GetEnvironmentVariable("BTO_LOGS_URL") ?? LogsUrl;

		// the lines that pair a player's name with their Steam ID (logs-worker/worker.js has the same list)
		static readonly Regex[] NameLines =
		{
			new Regex(@"User (?<id>7656119\d{10}) \((?<n>[^)\r\n]+)\)"),
			new Regex(@"from user (?<n>[^\[\r\n]+?) \[(?<id>7656119\d{10})\]"),
			new Regex(@"""(?<n>[^""\r\n]+)"" (?<id>7656119\d{10})"),
			new Regex(@"Username = (?<n>.+?),? ID = (?<id>7656119\d{10})"),
		};

		// Steam IDs and the names next to them become "Giocatore N" (the same N for the same player), the Windows user
		// folder becomes <utente>. Names and ids are collected from `whole` (the whole log), then replaced as whole words.
		public static string CleanLog(string text, string whole = null)
		{
			whole = whole ?? text;
			var label = new Dictionary<string, string>();
			foreach (Match m in Regex.Matches(whole, @"7656119\d{10}"))
				if (!label.ContainsKey(m.Value)) label[m.Value] = "Giocatore " + (label.Count + 1);
			var who = new Dictionary<string, string>();
			foreach (var r in NameLines)
				foreach (Match m in r.Matches(whole))
				{
					var n = m.Groups["n"].Value.Trim();
					if (n != "" && !who.ContainsKey(n)) who[n] = label[m.Groups["id"].Value];
				}
			foreach (var n in who.Keys.OrderByDescending(n => n.Length))
				text = Regex.Replace(text, @"(?<!\w)" + Regex.Escape(n) + @"(?!\w)", who[n]);
			text = Regex.Replace(text, @"7656119\d{10}", m => label.TryGetValue(m.Value, out var l) ? l : "Giocatore ?");
			return Regex.Replace(text, @"(?i)\b([a-z]):([\\/]+)Users[\\/]+[^\\/\r\n""]+", "$1:$2Users$2<utente>");
		}

		// The game to send: the run (from its "RNG Start Seed" line to the next one) with the first desync, else the last run.
		public static (string text, string seed) LastGame(string log)
		{
			var lines = log.Split('\n');
			bool Seed(string l) => l.Contains("RNG Start Seed:");
			int at = Array.FindIndex(lines, l => l.Contains("Desync detected on frame:"));
			int start = Math.Max(0, Array.FindLastIndex(lines, at < 0 ? lines.Length - 1 : at, Seed));
			int end = at < 0 ? -1 : Array.FindIndex(lines, at, Seed);
			if (end < 0) end = lines.Length;
			var m = Regex.Match(lines[start], @"RNG Start Seed: (\w{4}) (\w{4})");
			return (string.Join("\n", lines, start, end - start), m.Success ? m.Groups[1].Value + m.Groups[2].Value : "nessuno");
		}

		public static byte[] Gzip(string s)
		{
			var bytes = Encoding.UTF8.GetBytes(s);
			using (var ms = new MemoryStream())
			{
				using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal)) gz.Write(bytes, 0, bytes.Length);
				return ms.ToArray();
			}
		}

		// a random id per installation: groups one player's logs, says nothing about who they are
		static string InstallId()
		{
			var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BtoSync", "log-id.txt");
			try { var s = File.ReadAllText(f).Trim(); if (Regex.IsMatch(s, "^[0-9a-f]{12}$")) return s; } catch { }
			var id = Guid.NewGuid().ToString("N").Substring(0, 12);
			Directory.CreateDirectory(Path.GetDirectoryName(f));
			File.WriteAllText(f, id);
			return id;
		}

		// what the Worker receives: metadata and the cleaned game, gzip in base64
		public static string LogPayload(string log, DateTime logTimeUtc, IEnumerable<LocalMod> on)
		{
			var g = ReadGameLog(log);
			var (game, seed) = LastGame(log);
			var mods = on.Where(m => !m.Private).ToList();
			return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new
			{
				v = 1,
				app = Updater.Current.ToString(3),
				id = InstallId(),
				code = ToCode(mods),
				mods = mods.Select(m => new { folder = m.Folder, id = m.Id, name = m.Name, version = m.Version, hash = (m.Hash ?? "").Length > 8 ? m.Hash.Substring(0, 8) : m.Hash ?? "" }),
				seed,
				time = logTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),  // ponytail: the log has no clock: its last write (Isaac closed) stands for the game's time
				desyncFrame = g.FirstDesyncFrame,
				desyncs = g.Desyncs,
				luaErrors = g.LuaErrors,
				log = Convert.ToBase64String(Gzip(CleanLog(game, log + SteamUsers()))),
			});
		}

		// this PC's Steam accounts as name lines CleanLog knows, in case the game writes the own name without the id
		static string SteamUsers()
		{
			try
			{
				var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
				var vdf = File.ReadAllText(Path.Combine(steam, "config", "loginusers.vdf"));
				var s = new StringBuilder();
				foreach (Match u in Regex.Matches(vdf, @"""(7656119\d{10})""\s*\{([^}]*)\}"))
					foreach (Match n in Regex.Matches(u.Groups[2].Value, @"""(?:PersonaName|AccountName)""\s+""([^""\r\n]+)"""))
						s.Append($"\nUser {u.Groups[1].Value} ({n.Groups[1].Value})");
				return s.ToString();
			}
			catch { return ""; }
		}

		public static async Task SendLog(string payload)
		{
			using (var r = await Http.PostAsync(LogsUrlNow, new StringContent(payload, Encoding.UTF8, "application/json")))
				if (!r.IsSuccessStatusCode) throw new Exception($"il server dei log ha risposto {(int)r.StatusCode}");
		}

		public static Plan MakePlan(List<HostMod> host, List<LocalMod> local, Dictionary<string, string> latest)
		{
			var p = new Plan();
			var byKey = new Dictionary<string, LocalMod>();
			foreach (var l in local) if (!byKey.ContainsKey(l.Key)) byKey[l.Key] = l;
			var hostKeys = new HashSet<string>(host.Select(h => h.key));
			var maybeHostOld = new List<string>();  // BTO3 codes carry no manifest: one note for all of them
			foreach (var h in host)
			{
				byKey.TryGetValue(h.key, out var l);
				if (l != null && l.Link) { p.Notes.Add($"{h.name}: è un collegamento a una cartella di sviluppo, non la tocco."); continue; }
				string last = null;
				if (!string.IsNullOrEmpty(h.id)) latest.TryGetValue(h.id, out last);
				if (l != null && SameHash(l.Hash, h.hash))
				{
					if (!l.Enabled) p.Add(ActionKind.Enable, l, h, $"Attiva {h.name}");
					if (last != null && l.Manifest != "" && l.Manifest != last) p.Notes.Add($"{h.name}: uguale all'host, ma sul Workshop c'è un aggiornamento (per tutti).");
					continue;
				}
				if (IsBto(h.folder)) p.Add(ActionKind.Download, l, h, $"Scarica {h.name} {h.version} da GitHub");
				else if (!string.IsNullOrEmpty(h.id))
				{
					if (l == null) { p.Add(ActionKind.Subscribe, null, h, $"Installa {h.name} dal Workshop"); continue; }
					bool hostOld = last != null && !string.IsNullOrEmpty(h.manifest) && h.manifest != last, meOld = last != null && l.Manifest != last;
					if (hostOld) p.Notes.Add($"{h.name}: è l'host ad avere una versione vecchia: deve aggiornarla e ricopiare il codice.");
					else if (!meOld && string.IsNullOrEmpty(h.manifest) && last != null) maybeHostOld.Add(h.name);
					if (meOld || !hostOld) p.Add(ActionKind.Subscribe, l, h, (meOld ? "Aggiorna " : "Reinstalla ") + h.name + " dal Workshop");
					if (!l.Enabled) p.Add(ActionKind.Enable, l, h, $"Attiva {h.name}");
				}
				else p.Notes.Add($"{h.name} ({h.folder}): non è né sul Workshop né su GitHub: chiedi la cartella all'host.");
			}
			if (maybeHostOld.Count > 0)
				p.Notes.Add($"{string.Join(", ", maybeHostOld)}: {(maybeHostOld.Count == 1 ? "hai già l'ultima versione" : "hai già le ultime versioni")} del Workshop ma i file sono diversi da quelli dell'host. Le reinstallo; se restano diverse, è l'host a dover aggiornare e ricopiare il codice.");
			foreach (var t in Ties.Where(t => host.Any(h => h.id == t.Value.id) && !host.Any(h => h.folder == t.Key)))
				p.Notes.Add($"L'host ha {t.Value.name} ma non {t.Key}: deve aggiornare BTO Sync e ricopiare il codice.");
			foreach (var l in local.Where(x => x.Enabled && !hostKeys.Contains(x.Key) && !x.Private))
			{
				if (l.Link) p.Notes.Add($"{l.Name}: l'host non ce l'ha, ma è un collegamento: disattivala dal menu Mods del gioco.");
				else p.Add(ActionKind.Disable, l, null, $"Disattiva {l.Name} (l'host non ce l'ha)");
			}
			return p;
		}
	}
}
