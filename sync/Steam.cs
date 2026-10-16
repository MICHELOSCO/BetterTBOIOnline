using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Ugc;

namespace BtoSync
{
	// Workshop through the local Steam client, signed in as Isaac (app 250900): no API key, the user's own subscriptions.
	// While connected Steam shows the user "in game" in Isaac, so connect only when needed and disconnect before launching it.
	public static class Steam
	{
		public static bool Ready => SteamClient.IsValid;

		public static string Connect()
		{
			if (SteamClient.IsValid) return null;
			if (Core.IsaacRunning) return "Chiudi Isaac per usare il Workshop da qui.";
			try { SteamClient.Init(Core.AppId, true); return null; }
			catch (Exception e) { return "Non riesco a collegarmi a Steam: aprilo, accedi e riprova. (" + e.Message + ")"; }
		}

		public static void Disconnect() { if (SteamClient.IsValid) SteamClient.Shutdown(); }

		// sort: 0 trend (over `days`, -1 = all time), 1 best rated, 2 most subscribed, 3 newest, 4 last updated, 5 relevance (text only).
		// tags: the item must have all of them (as Steam's "requiredtags"). The text filters any sort.
		public static async Task<(List<Item> items, int total)> Search(string text, int sort, int days, IEnumerable<string> tags, int page)
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

		// Subscribes (if needed), lets Steam download the latest version, then copies it into mods/ like Isaac does.
		public static async Task SubscribeAndInstall(ulong id, string modsDir, Action<float> progress)
		{
			var it = await Item.GetAsync(id, 0);
			if (it == null) throw new Exception("mod non trovata sul Workshop");
			if (!it.Value.IsSubscribed && !await it.Value.Subscribe()) throw new Exception("Steam ha rifiutato l'iscrizione");
			if (!await SteamUGC.DownloadAsync(id, progress, 200)) throw new Exception("download non riuscito");
			var dir = (await Item.GetAsync(id, 0))?.Directory;
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) throw new Exception("download finito ma Steam non dice dove: riprova");
			Core.InstallWorkshopCopy(dir, id.ToString(), modsDir);
		}

		public static async Task Unsubscribe(ulong id)
		{
			var it = await Item.GetAsync(id, 0);
			if (it != null && it.Value.IsSubscribed) await it.Value.Unsubscribe();
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
		public string[] Tags { get; set; } = new string[0];  // Italian labels
		bool selected; string requires = "";
		public bool IsSelected { get => selected; set => Set(ref selected, value); }
		public string RequiresText { get => requires; set { Set(ref requires, value); Raise(nameof(HasRequires)); } }
		public bool HasRequires => requires != "";
		public string Initial => string.IsNullOrEmpty(Title) ? "?" : Title.Substring(0, 1).ToUpperInvariant();
		bool installed, busy; double progress; string error = "";
		public bool Installed { get => installed; set { Set(ref installed, value); Raise(nameof(ButtonText)); } }
		public bool Busy { get => busy; set { Set(ref busy, value); Raise(nameof(ButtonText)); Raise(nameof(CanAct)); } }
		public double Progress { get => progress; set { Set(ref progress, value); Raise(nameof(ButtonText)); } }
		public string Error { get => error; set => Set(ref error, value); }
		public bool CanAct => !Busy;
		public string ButtonText => Busy ? (Progress > 0 ? $"Scarico {Progress:P0}" : "Attendi…") : Installed ? "Disinstalla" : "Installa";

		static string Short(string s) => s == null ? null : s.Length > 300 ? s.Substring(0, 300) + "…" : s;

		static string Compact(ulong n) => n >= 1000000 ? $"{n / 1000000.0:0.#} mln" : n >= 1000 ? $"{n / 1000.0:0.#}k" : n.ToString();
		static string Size(long b) => b <= 0 ? null : b >= 1 << 30 ? $"{b / (double)(1 << 30):0.#} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0.#} MB" : $"{Math.Max(1, b / 1024)} KB";

		const int MinVotes = 10;  // below this a percentage is noise ("100%" from 3 votes)

		// Facepunch 2.3.3 keeps Steam's "number of required items" only in its internal details struct.
		static readonly System.Reflection.FieldInfo DetailsField = typeof(Item).GetField("details", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		static int Children(Item i)
		{
			try
			{
				var d = DetailsField?.GetValue(i);
				return d == null ? 0 : Convert.ToInt32(d.GetType().GetField("NumChildren", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(d) ?? 0);
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
				SizeText = Size(i.SizeBytes) ?? "-", UpdatedText = $"{i.Updated:dd/MM/yyyy}",
				KindText = tags.Length == 0 ? "" : visual ? "Solo grafica e suoni" : "Può cambiare il gameplay", IsVisualOnly = visual,
				NumRequired = Children(i),
				Tags = tags.Select(TagChip.LabelOf).ToArray(),
				Installed = Core.HasWorkshopCopy(i.Id.Value.ToString(), modsDir),
			};
		}
	}
}
