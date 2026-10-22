using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Steamworks;
using Steamworks.Ugc;

namespace BtoSync
{
	// Workshop through the local Steam client, signed in as Isaac (app 250900): no API key, the user's own subscriptions.
	// Steam marks Isaac as running from SteamClient.Init until the process that called it exits: SteamClient.Shutdown is not
	// enough. So the Steamworks calls run in a child "BTO-Sync.exe --steam-host" (Host below), one JSON request per line, and the
	// child exits on Disconnect, after a minute without requests, and with this process (its stdin closes).
	public static class Steam
	{
		const int IdleMs = 60000;
		static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
		static readonly object gate = new object();
		static readonly Dictionary<int, Action<Dictionary<string, object>>> waiting = new Dictionary<int, Action<Dictionary<string, object>>>();
		static Process host; static StreamWriter toHost; static int nextId; static Timer idle;

		public static event Action Changed;  // connected or not: on a background thread
		public static bool Ready { get { lock (gate) return host != null && !host.HasExited; } }

		public static string Connect()
		{
			if (Ready) return null;
			if (Core.IsaacRunning) return "Chiudi Isaac per usare il Workshop da qui.";
			const string fail = "Non riesco a collegarmi a Steam: aprilo, accedi e riprova.";
			try
			{
				var p = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--steam-host")
				{
					UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, StandardOutputEncoding = new UTF8Encoding(false),
				});
				// "ok" once SteamClient.Init worked, "err:" and why if not; steam_api prints lines of its own on the same output: skipped
				var first = Task.Run(() => { string l; while ((l = p.StandardOutput.ReadLine()) != null && l != "ok" && !l.StartsWith("err:")) { } return l; });
				if (!first.Wait(15000) || first.Result != "ok")
				{
					try { p.Kill(); } catch { }
					return fail + (first.IsCompleted && first.Result != null ? " (" + first.Result.Substring(4).Trim() + ")" : "");
				}
				lock (gate) { host = p; toHost = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true }; }
				new Thread(() => Read(p)) { IsBackground = true }.Start();
				Touch();
				Changed?.Invoke();
				return null;
			}
			catch (Exception e) { return fail + " (" + e.Message + ")"; }
		}

		// Ends the child; true when there was one, so Steam is about to see Isaac closed (WaitReleased).
		public static bool Disconnect()
		{
			Process p;
			lock (gate) { p = host; host = null; try { toHost?.Close(); } catch { } toHost = null; idle?.Dispose(); idle = null; }
			if (p == null) return false;
			try { if (!p.WaitForExit(3000)) p.Kill(); } catch { }
			Changed?.Invoke();
			return true;
		}

		// After Disconnect Steam takes a moment to mark Isaac as closed: launching before that only brings up "already running".
		public static async Task WaitReleased(int ms = 6000)
		{
			for (int t = 0; t < ms; t += 250)
			{
				var running = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\Apps\" + Core.AppId, "Running", 0);
				if (!(running is int r) || r == 0) return;
				await Task.Delay(250);
			}
		}

		static void Touch()
		{
			lock (gate)
			{
				idle?.Dispose();
				idle = new Timer(_ => { bool busy; lock (gate) busy = waiting.Count > 0; if (busy) Touch(); else Disconnect(); }, null, IdleMs, Timeout.Infinite);
			}
		}

		static void Read(Process p)
		{
			string line;
			try
			{
				while ((line = p.StandardOutput.ReadLine()) != null)
				{
					Dictionary<string, object> m;
					try { m = Json.Deserialize<Dictionary<string, object>>(line); } catch { continue; }
					Action<Dictionary<string, object>> h = null;
					lock (gate) if (m != null && m.TryGetValue("rid", out var rid)) waiting.TryGetValue(Convert.ToInt32(rid), out h);
					h?.Invoke(m);
				}
			}
			catch { }
			List<Action<Dictionary<string, object>>> left;
			bool mine;
			lock (gate) { mine = host == p; if (mine) { host = null; toHost = null; } left = mine ? waiting.Values.ToList() : new List<Action<Dictionary<string, object>>>(); if (mine) waiting.Clear(); }
			foreach (var h in left) h(new Dictionary<string, object> { ["err"] = "Steam si è scollegato: riprova" });
			if (mine) Changed?.Invoke();
		}

		static Task<Dictionary<string, object>> Ask(Dictionary<string, object> req, Action<float> progress = null)
		{
			var err = Connect();
			if (err != null) throw new Exception(err);
			var done = new TaskCompletionSource<Dictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
			lock (gate)
			{
				if (toHost == null) throw new Exception("Steam si è scollegato: riprova");
				int id = ++nextId;
				req["rid"] = id;
				waiting[id] = m =>
				{
					if (m.TryGetValue("p", out var pr)) { progress?.Invoke(Convert.ToSingle(pr)); return; }
					lock (gate) waiting.Remove(id);
					Touch();
					if (m.TryGetValue("err", out var e)) done.TrySetException(new Exception(e as string ?? "errore di Steam"));
					else done.TrySetResult(m);
				};
				toHost.WriteLine(Json.Serialize(req));
			}
			return done.Task;
		}

		// sort: 0 trend (over `days`, -1 = all time), 1 best rated, 2 most subscribed, 3 newest, 4 last updated, 5 relevance (text only).
		// tags: the item must have all of them (as Steam's "requiredtags"). The text filters any sort.
		public static async Task<(List<WsItem> items, int total)> Search(string text, int sort, int days, IEnumerable<string> tags, int page)
		{
			var r = await Ask(new Dictionary<string, object> { ["op"] = "search", ["text"] = text, ["sort"] = sort, ["days"] = days, ["tags"] = tags.ToArray(), ["page"] = page });
			var items = ((ArrayList)r["items"]).Cast<Dictionary<string, object>>().Select(d => Json.ConvertToType<WsItem>(d)).ToList();
			return (items, Convert.ToInt32(r["total"]));
		}

		// Subscribes (if needed), lets Steam download the latest version, then copies it into mods/ like Isaac does.
		public static async Task SubscribeAndInstall(ulong id, string modsDir, Action<float> progress)
		{
			var r = await Ask(new Dictionary<string, object> { ["op"] = "install", ["id"] = id.ToString() }, progress);
			Core.InstallWorkshopCopy((string)r["dir"], id.ToString(), modsDir);
		}

		public static Task Unsubscribe(ulong id) => Ask(new Dictionary<string, object> { ["op"] = "unsub", ["id"] = id.ToString() });

		// ---- the child's side: BTO-Sync.exe --steam-host ----
		public static int Host()
		{
			var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
			var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
			var w = new object();
			void Send(object o) { var line = o as string ?? Json.Serialize(o); lock (w) output.WriteLine(line); }
			try { SteamClient.Init(Core.AppId, true); }
			catch (Exception e) { Send("err: " + e.Message.Replace('\n', ' ')); return 1; }
			Send("ok");
			string req;
			while ((req = input.ReadLine()) != null)  // null: the parent disconnected or closed
			{
				var q = Json.Deserialize<Dictionary<string, object>>(req);
				var rid = q["rid"];
				Task.Run(async () =>
				{
					Dictionary<string, object> a;
					try { a = await Do(q, p => Send(new Dictionary<string, object> { ["rid"] = rid, ["p"] = p })); }
					catch (Exception e) { a = new Dictionary<string, object> { ["err"] = e.Message }; }
					a["rid"] = rid;
					Send(a);
				});
			}
			SteamClient.Shutdown();
			return 0;
		}

		static async Task<Dictionary<string, object>> Do(Dictionary<string, object> q, Action<float> progress)
		{
			var id = q.TryGetValue("id", out var i) ? ulong.Parse((string)i) : 0;
			switch ((string)q["op"])
			{
				case "search":
					var (items, total) = await SearchHere(q["text"] as string, Convert.ToInt32(q["sort"]), Convert.ToInt32(q["days"]),
						((ArrayList)q["tags"]).Cast<string>(), Convert.ToInt32(q["page"]));
					return new Dictionary<string, object> { ["items"] = items.Select(x => Data(WsItem.From(x, null))).ToList(), ["total"] = total };
				case "install":
					return new Dictionary<string, object> { ["dir"] = await Download(id, progress) };
				case "unsub":
					var it = await Item.GetAsync(id, 0);
					if (it != null && it.Value.IsSubscribed) await it.Value.Unsubscribe();
					return new Dictionary<string, object>();
			}
			throw new Exception("richiesta sconosciuta");
		}

		// what crosses to the parent: the fields WsItem.From fills (Installed is the parent's: it knows the mods folder)
		static Dictionary<string, object> Data(WsItem w) => new Dictionary<string, object>
		{
			["Id"] = w.Id, ["Title"] = w.Title, ["Preview"] = w.Preview, ["LongDescription"] = w.LongDescription, ["Description"] = w.Description,
			["SubsText"] = w.SubsText, ["HasScore"] = w.HasScore, ["ScoreText"] = w.ScoreText, ["VotesText"] = w.VotesText, ["SubsShort"] = w.SubsShort,
			["SubsFull"] = w.SubsFull, ["SizeText"] = w.SizeText, ["UpdatedText"] = w.UpdatedText, ["KindText"] = w.KindText,
			["IsVisualOnly"] = w.IsVisualOnly, ["NumRequired"] = w.NumRequired, ["Tags"] = w.Tags, ["NeedsRepentogon"] = w.NeedsRepentogon,
		};

		static async Task<(List<Item> items, int total)> SearchHere(string text, int sort, int days, IEnumerable<string> tags, int page)
		{
			var q = Query.ItemsReadyToUse.WithLongDescription(true).WithChildren(true).MatchAllTags();  // children: fills the required-items count
			foreach (var t in tags) q = q.WithTag(t);
			bool hasText = !string.IsNullOrWhiteSpace(text);
			if (hasText) q = q.WhereSearchText(text.Trim());
			if (sort == 5 && hasText) q = q.RankedByTextSearch();
			else if (sort == 1) q = q.RankedByVote();
			else if (sort == 2) q = q.RankedByTotalUniqueSubscriptions();
			else if (sort == 3) q = q.RankedByPublicationDate();
			else if (sort == 4) q = q.SortByUpdateDate();
			else q = q.RankedByTrend().WithTrendDays(days);
			var r = await q.GetPageAsync(page);
			if (r == null) throw new Exception("il Workshop non ha risposto, riprova");
			using (var p = r.Value) return (p.Entries.ToList(), p.TotalCount);
		}

		// Subscribes (if needed) and lets Steam download the latest version; returns where Steam put it.
		static async Task<string> Download(ulong id, Action<float> progress)
		{
			var it = await Item.GetAsync(id, 0);
			if (it == null) throw new Exception("mod non trovata sul Workshop");
			if (!it.Value.IsSubscribed && !await it.Value.Subscribe()) throw new Exception("Steam ha rifiutato l'iscrizione");
			if (!await SteamUGC.DownloadAsync(id, progress, 200)) throw new Exception("download non riuscito");
			var dir = (await Item.GetAsync(id, 0))?.Directory;
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) throw new Exception("download finito ma Steam non dice dove: riprova");
			return dir;
		}
	}

	// One of Steam's Workshop tags for Isaac, as a filter chip: Name is Steam's value, Label the Italian one.
	public class TagChip : Notify
	{
		bool on;
		public string Name { get; set; }
		public string Label { get; set; }
		public bool On { get => on; set => Set(ref on, value); }
		// the tags of Isaac's Workshop page (steamcommunity.com/workshop/browse/?appid=250900), in its order
		public static readonly (string name, string label)[] All =
		{
			("Lua", "Lua"), ("Items", "Oggetti"), ("Active Items", "Oggetti attivi"), ("Trinkets", "Trinket"), ("Pills", "Pillole"), ("Cards", "Carte"),
			("Pickups", "Raccoglibili"), ("Player Characters", "Personaggi"), ("Familiars", "Famigli"), ("Babies", "Bambini co-op"), ("Rooms", "Stanze"),
			("Floors", "Piani"), ("Enemies", "Nemici"), ("Bosses", "Boss"), ("Hazards", "Ostacoli"), ("Challenges", "Sfide"), ("Tweaks", "Ritocchi"),
			("Removals", "Rimozioni"), ("Graphics", "Grafica"), ("Shaders", "Shader"), ("Sound Effects", "Effetti sonori"), ("Music", "Musica"),
		};
		public static string LabelOf(string tag) => All.FirstOrDefault(t => t.name.Equals(tag, StringComparison.OrdinalIgnoreCase)).label ?? tag;
		// tags that only change looks and sound; Steam tags are set by each mod's author
		public static readonly string[] Visual = { "Graphics", "Shaders", "Sound Effects", "Music" };
	}

	public class WsItem : Notify
	{
		public ulong Id; public string Title { get; set; } public string Preview { get; set; } public string Description { get; set; }
		public string SubsText { get; set; } public string LongDescription { get; set; }  // a property: WPF bindings ignore fields
		public bool Skeleton { get; set; }      // placeholder tile while a search runs
		public string ScoreText { get; set; }   // "92%" positive votes
		public bool HasScore { get; set; }      // enough votes for the percentage to mean something
		public string SubsShort { get; set; }   // "3,1k"
		public string SubsFull { get; set; }
		public string VotesText { get; set; }
		public string SizeText { get; set; }
		public string UpdatedText { get; set; }
		public string KindText { get; set; } = "";  // "Solo grafica e suoni" / "Può cambiare il gameplay", from the Steam tags
		public bool IsVisualOnly { get; set; }
		public bool HasKind => KindText != "";
		public int NumRequired { get; set; }    // Steam "Required items" count
		public bool NeedsOthers => NumRequired > 0;
		public bool NeedsRepentogon { get; set; }  // Core.RequiresRepentogon on what the author wrote
		string rgonNote = "";
		public string RepentogonNote { get => rgonNote; set { Set(ref rgonNote, value); Raise(nameof(HasRepentogonNote)); } }  // MainWindow.MarkWs: have it or not
		public bool HasRepentogonNote => rgonNote != "";
		public string[] Tags { get; set; } = new string[0];  // Italian labels
		bool selected; string requires = "";
		public bool IsSelected { get => selected; set => Set(ref selected, value); }
		public string RequiresText { get => requires; set { Set(ref requires, value); Raise(nameof(HasRequires)); } }
		public bool HasRequires => requires != "";
		public string Initial => string.IsNullOrEmpty(Title) ? "?" : Title.Substring(0, 1).ToUpperInvariant();
		bool installed, active, busy; double progress; string error = "";
		public bool Installed { get => installed; set { Set(ref installed, value); Raise(nameof(ButtonText)); } }
		public bool Active { get => active; set { Set(ref active, value); Raise(nameof(ButtonText)); } }  // installed and on now, so in the active profile's mods
		public bool Busy { get => busy; set { Set(ref busy, value); Raise(nameof(ButtonText)); Raise(nameof(CanAct)); } }
		public double Progress { get => progress; set { Set(ref progress, value); Raise(nameof(ButtonText)); } }
		public string Error { get => error; set => Set(ref error, value); }
		public bool CanAct => !Busy;
		public string ButtonText => Busy ? (Progress > 0 ? $"Scarico {Progress:P0}" : "Attendi…") : !Installed ? "Installa" : !Active ? "Attiva" : "Disinstalla";

		static string Short(string s) => s == null ? null : s.Length > 300 ? s.Substring(0, 300) + "…" : s;

		static string Compact(ulong n) => n >= 1000000 ? $"{n / 1000000.0:0.#} mln" : n >= 1000 ? $"{n / 1000.0:0.#}k" : n.ToString();
		static string Size(long b) => b <= 0 ? null : b >= 1 << 30 ? $"{b / (double)(1 << 30):0.#} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0.#} MB" : $"{Math.Max(1, b / 1024)} KB";

		const int MinVotes = 10;  // below this a percentage is noise ("100%" from 3 votes)

		// Facepunch 2.3.3 keeps Steam's "number of required items" and published size only in its internal details struct
		// (its SizeBytes is the installed size: 0 for anything not downloaded).
		static readonly System.Reflection.FieldInfo DetailsField = typeof(Item).GetField("details", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		static long Detail(Item i, string field)
		{
			try
			{
				var d = DetailsField?.GetValue(i);
				return d == null ? 0 : Convert.ToInt64(d.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(d) ?? 0);
			}
			catch { return 0; }
		}

		public static WsItem From(Item i, string modsDir)
		{
			uint votes = i.VotesUp + i.VotesDown;
			var tags = i.Tags ?? new string[0];
			bool visual = tags.Length > 0 && tags.All(t => TagChip.Visual.Contains(t, StringComparer.OrdinalIgnoreCase));
			var desc = Core.CleanDescription(i.Description);
			return new WsItem
			{
				Id = i.Id.Value,
				Title = i.Title?.Trim(),
				Preview = string.IsNullOrEmpty(i.PreviewImageUrl) ? null : i.PreviewImageUrl,
				LongDescription = string.IsNullOrWhiteSpace(desc) ? "L'autore non ha scritto una descrizione (solo immagini): la trovi sulla pagina Workshop." : desc,
				Description = Short(desc),
				SubsText = $"{i.NumUniqueSubscriptions:N0} iscritti · aggiornata {i.Updated:dd/MM/yyyy}",
				HasScore = votes >= MinVotes, ScoreText = votes == 0 ? "" : $"{100.0 * i.VotesUp / votes:0}%",
				VotesText = votes == 0 ? "nessun voto" : votes < MinVotes ? $"pochi voti ({votes})" : $"{100.0 * i.VotesUp / votes:0}% di {votes:N0}",
				SubsShort = Compact(i.NumUniqueSubscriptions), SubsFull = $"{i.NumUniqueSubscriptions:N0}",
				SizeText = Size(Math.Max(Detail(i, "FileSize"), i.SizeBytes)) ?? "-", UpdatedText = $"{i.Updated:dd/MM/yyyy}",
				KindText = tags.Length == 0 ? "" : visual ? "Solo grafica e suoni" : "Può cambiare il gameplay", IsVisualOnly = visual,
				NumRequired = (int)Detail(i, "NumChildren"),
				NeedsRepentogon = Core.RequiresRepentogon(i.Title, desc),
				Tags = tags.Select(TagChip.LabelOf).ToArray(),
				Installed = Core.HasWorkshopCopy(i.Id.Value.ToString(), modsDir),
			};
		}
	}
}
