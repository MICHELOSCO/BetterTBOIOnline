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

		public static async Task<(List<Item> items, int total)> Search(string text, int sort, int page)
		{
			var q = Query.ItemsReadyToUse.WithLongDescription(false);
			if (!string.IsNullOrWhiteSpace(text)) q = q.WhereSearchText(text.Trim()).RankedByTextSearch();
			else if (sort == 1) q = q.RankedByTotalUniqueSubscriptions();
			else if (sort == 2) q = q.RankedByPublicationDate();
			else q = q.RankedByTrend();
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

	public class WsItem : Notify
	{
		public ulong Id; public string Title { get; set; } public string Preview { get; set; } public string Description { get; set; }
		public string SubsText { get; set; }
		bool installed, busy; double progress; string error = "";
		public bool Installed { get => installed; set { Set(ref installed, value); Raise(nameof(ButtonText)); } }
		public bool Busy { get => busy; set { Set(ref busy, value); Raise(nameof(ButtonText)); Raise(nameof(CanAct)); } }
		public double Progress { get => progress; set { Set(ref progress, value); Raise(nameof(ButtonText)); } }
		public string Error { get => error; set => Set(ref error, value); }
		public bool CanAct => !Busy;
		public string ButtonText => Busy ? (Progress > 0 ? $"Scarico {Progress:P0}" : "Attendi…") : Installed ? "Rimuovi" : "Installa";

		public static WsItem From(Item i, string modsDir) => new WsItem
		{
			Id = i.Id.Value,
			Title = i.Title?.Trim(),
			Preview = string.IsNullOrEmpty(i.PreviewImageUrl) ? null : i.PreviewImageUrl,
			Description = string.IsNullOrWhiteSpace(i.Description) ? null : (i.Description.Length > 400 ? i.Description.Substring(0, 400) + "…" : i.Description),
			SubsText = $"{i.NumUniqueSubscriptions:N0} iscritti · aggiornata {i.Updated:dd/MM/yyyy}",
			Installed = Core.HasWorkshopCopy(i.Id.Value.ToString(), modsDir),
		};
	}
}
