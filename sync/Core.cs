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

	// One entry of the host code (lowercase names: JSON of the BTO1 code).
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
		public string Key, Folder, Name, Id, Version, Hash, Manifest, Path;
		public bool Link;
		bool enabled; string status = ""; Brush statusBrush = Theme.Muted; bool canUpdate;
		public string NameText => Name;
		public string FolderText => Folder;
		public string VersionText => Version == "" ? "" : "v" + Version;
		public string Origin => Link ? "Collegamento" : Id != "" ? "Workshop" : Core.IsBto(Folder) ? "BTO" : "Locale";
		public bool Enabled { get => enabled; set => Set(ref enabled, value); }
		public string Status { get => status; set => Set(ref status, value); }
		public Brush StatusBrush { get => statusBrush; set => Set(ref statusBrush, value); }
		public bool CanUpdate { get => canUpdate; set => Set(ref canUpdate, value); }

		public void UpdateStatus(Dictionary<string, string> latest)
		{
			CanUpdate = false; StatusBrush = Theme.Muted;
			if (Link) Status = "Collegata a una cartella di sviluppo";
			else if (Id == "") Status = Core.IsBto(Folder) ? "Fix BetterTBOIOnline" : "Solo su questo PC";
			else if (!latest.TryGetValue(Id, out var last)) Status = "";
			else if (Manifest != "" && Manifest != last) { Status = "Aggiornamento disponibile"; StatusBrush = Theme.Warn; CanUpdate = true; }
			else { Status = "Aggiornata"; StatusBrush = Theme.Ok; }
		}
	}

	public enum ActionKind { Enable, Disable, Download, Subscribe }

	public class SyncAction : Notify
	{
		public ActionKind Kind; public LocalMod Local; public HostMod Host;
		string state = "";
		public string Text { get; set; }
		public string KindText => Kind switch { ActionKind.Enable => "ATTIVA", ActionKind.Disable => "DISATTIVA", ActionKind.Download => "SCARICA", _ => "WORKSHOP" };
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
		public static readonly Brush Muted = Freeze("#9A9CA5"), Ok = Freeze("#5FC27E"), Warn = Freeze("#E8B04A"), Bad = Freeze("#FF7B6B");
		static Brush Freeze(string hex) { var b = (Brush)new BrushConverter().ConvertFrom(hex); b.Freeze(); return b; }
	}

	public static class Core
	{
		public const string Repo = "MICHELOSCO/BetterTBOIOnline";
		public const uint AppId = 250900;
		static readonly string[] TextExt = { ".lua", ".xml", ".txt", ".json", ".md", ".anm2", ".ini", ".csv" };
		static readonly HttpClient Http = MakeHttp();

		static HttpClient MakeHttp()
		{
			ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
			var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
			h.DefaultRequestHeaders.UserAgent.ParseAdd("bto-sync");
			return h;
		}

		public static bool IsaacRunning => Process.GetProcessesByName("isaac-ng").Length > 0;
		public static bool IsBto(string folder) => folder.StartsWith("bto-", StringComparison.OrdinalIgnoreCase) || folder.Equals("bettertboionline-diagnostics", StringComparison.OrdinalIgnoreCase);
		static bool IsLink(string dir) => new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint);

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

		// Short hash of a mod's files (paths + contents, disable.it excluded), equal on every PC with the same files.
		// Text files are hashed without CR: git autocrlf, zips and editors change line endings without changing the mod.
		public static string ModHash(string dir)
		{
			var root = System.IO.Path.GetFullPath(dir).TrimEnd('\\');
			var lines = new List<string>();
			using (var sha = SHA256.Create())
			{
				foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
				{
					if (System.IO.Path.GetFileName(f).Equals("disable.it", StringComparison.OrdinalIgnoreCase)) continue;
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

		static (string name, string id, string version, string directory) ReadMeta(string dir)
		{
			try
			{
				var x = XDocument.Load(System.IO.Path.Combine(dir, "metadata.xml")).Root;
				string V(string n) => (x.Element(n)?.Value ?? "").Trim();
				return (V("name"), V("id"), V("version"), V("directory"));
			}
			catch { return ("", "", "", ""); }
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
					Path = d,
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

		public static string ToCode(IEnumerable<LocalMod> mods)
		{
			var dto = new CodeDto { v = 1, mods = mods.Select(m => new HostMod { key = m.Key, folder = m.Folder, name = m.Name, id = m.Id, version = m.Version, hash = m.Hash, manifest = m.Manifest }).ToList() };
			var bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(dto));
			using (var ms = new MemoryStream())
			{
				using (var gz = new GZipStream(ms, CompressionMode.Compress)) gz.Write(bytes, 0, bytes.Length);
				return "BTO1:" + Convert.ToBase64String(ms.ToArray());
			}
		}

		public static List<HostMod> FromCode(string code)
		{
			code = Regex.Replace(code ?? "", @"[\s`]", "");  // Discord may wrap it in backticks
			if (!code.StartsWith("BTO1:")) throw new FormatException("Il codice deve iniziare con BTO1: fattelo ricopiare dall'host.");
			try
			{
				using (var gz = new GZipStream(new MemoryStream(Convert.FromBase64String(code.Substring(5))), CompressionMode.Decompress))
				using (var r = new StreamReader(gz, Encoding.UTF8))
					return new JavaScriptSerializer().Deserialize<CodeDto>(r.ReadToEnd()).mods ?? new List<HostMod>();
			}
			catch (Exception e) when (!(e is FormatException && e.Message.StartsWith("Il codice")))
			{ throw new FormatException("Codice incompleto o rovinato: copialo di nuovo tutto, da BTO1: alla fine."); }
		}

		static System.Collections.IEnumerable Arr(object o) => o as System.Collections.IEnumerable ?? new object[0];

		// Latest Workshop manifest (hcontent_file) per item, from the keyless Steam Web API.
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
				if (d.TryGetValue("hcontent_file", out var h)) r[d["publishedfileid"].ToString()] = h.ToString();
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
				return ModHash(dst) == h.hash ? null : $"la copia dell'host è diversa dalla release {h.version}: chiedigli la cartella";
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

		public static Plan MakePlan(List<HostMod> host, List<LocalMod> local, Dictionary<string, string> latest)
		{
			var p = new Plan();
			var byKey = new Dictionary<string, LocalMod>();
			foreach (var l in local) if (!byKey.ContainsKey(l.Key)) byKey[l.Key] = l;
			var hostKeys = new HashSet<string>(host.Select(h => h.key));
			foreach (var h in host)
			{
				byKey.TryGetValue(h.key, out var l);
				if (l != null && l.Link) { p.Notes.Add($"{h.name}: è un collegamento a una cartella di sviluppo, non la tocco."); continue; }
				string last = null;
				if (!string.IsNullOrEmpty(h.id)) latest.TryGetValue(h.id, out last);
				if (l != null && l.Hash == h.hash)
				{
					if (!l.Enabled) p.Add(ActionKind.Enable, l, h, $"Attiva {h.name}");
					if (last != null && l.Manifest != "" && l.Manifest != last) p.Notes.Add($"{h.name}: uguale all'host, ma sul Workshop c'è un aggiornamento (per tutti).");
					continue;
				}
				if (IsBto(h.folder)) p.Add(ActionKind.Download, l, h, $"Scarica {h.name} {h.version} da GitHub");
				else if (!string.IsNullOrEmpty(h.id))
				{
					if (l == null) { p.Add(ActionKind.Subscribe, null, h, $"Installa {h.name} dal Workshop"); continue; }
					bool hostOld = last != null && h.manifest != last, meOld = last != null && l.Manifest != last;
					if (hostOld) p.Notes.Add($"{h.name}: è l'host ad avere una versione vecchia: deve aggiornarla e ricopiare il codice.");
					if (meOld || !hostOld) p.Add(ActionKind.Subscribe, l, h, (meOld ? "Aggiorna " : "Reinstalla ") + h.name + " dal Workshop");
					if (!l.Enabled) p.Add(ActionKind.Enable, l, h, $"Attiva {h.name}");
				}
				else p.Notes.Add($"{h.name} ({h.folder}): non è né sul Workshop né su GitHub: chiedi la cartella all'host.");
			}
			foreach (var l in local.Where(x => x.Enabled && !hostKeys.Contains(x.Key)))
			{
				if (l.Link) p.Notes.Add($"{l.Name}: l'host non ce l'ha, ma è un collegamento: disattivala dal menu Mods del gioco.");
				else p.Add(ActionKind.Disable, l, null, $"Disattiva {l.Name} (l'host non ce l'ha)");
			}
			return p;
		}
	}
}
