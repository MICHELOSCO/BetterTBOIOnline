using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BtoSync
{
	public partial class MainWindow : Window
	{
		string modsDir;
		List<LocalMod> local = new List<LocalMod>();
		readonly ObservableCollection<LocalMod> mods = new ObservableCollection<LocalMod>();
		readonly ObservableCollection<WsItem> ws = new ObservableCollection<WsItem>();
		readonly ObservableCollection<Profile> profiles = new ObservableCollection<Profile>();
		string activeProfile;
		Plan plan;
		int wsPage = 1;
		bool wsOpened, isaac, busy;

		public MainWindow()
		{
			InitializeComponent();
			ModsList.ItemsSource = mods;
			WsList.ItemsSource = ws;
			ProfilesList.ItemsSource = profiles;
			CollectionViewSource.GetDefaultView(mods).Filter = ModFilter;
			AboutText.Text = "BTO Sync " + typeof(MainWindow).Assembly.GetName().Version.ToString(3);
			SideVersion.Text = "v" + Updater.Current.ToString(3);
			Title = AboutText.Text;
			InfoVersion.Text = AboutText.Text + " · gestore di mod open source del progetto BetterTBOIOnline, per giocare online con le stesse mod degli amici.";
			modsDir = Core.FindModsDir();
			var (list, active) = Core.LoadProfiles();
			foreach (var p in list) profiles.Add(p);
			activeProfile = active;
			var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
			timer.Tick += (s, e) => UpdateState();
			timer.Start();
			RefreshProfiles();
			Loaded += async (s, e) => { UpdateState(); _ = CheckUpdate(); await Reload(); CheckPatch(); OpenHostCodeFromClipboard(); };
			Closing += (s, e) => Steam.Disconnect();
			KeyDown += (s, e) =>  // Ctrl+F: search the page that has a search box
			{
				if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;
				if (NavWs.IsChecked != true) NavMods.IsChecked = true;
				(NavWs.IsChecked == true ? SearchBox : ModsSearch).Focus();
				e.Handled = true;
			};
			SourceInitialized += (s, e) => { int on = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref on, 4); };  // dark title bar
		}

		[System.Runtime.InteropServices.DllImport("dwmapi.dll")]
		static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

		static T Ctx<T>(object sender) => (T)((FrameworkElement)sender).DataContext;
		void Status(string s) => StatusText.Text = s;

		void UpdateState()
		{
			isaac = Core.IsaacRunning;
			if (isaac && Steam.Ready) Steam.Disconnect();  // Isaac needs Steam to itself
			IsaacBanner.Visibility = isaac ? Visibility.Visible : Visibility.Collapsed;
			SteamBox.Visibility = SettingsDisconnect.Visibility = Steam.Ready ? Visibility.Visible : Visibility.Collapsed;
			foreach (var m in mods) m.Unlocked = !isaac && !busy;
			PatchBtn.IsEnabled = PatchBannerBtn.IsEnabled = !isaac && !busy;
			ApplyBtn.IsEnabled = !isaac && !busy;
			LaunchBtn.IsEnabled = PlanLaunchBtn.IsEnabled = !isaac;
		}

		bool Blocked()
		{
			UpdateState();
			if (isaac) { Dialog.Show(this, "Chiudi Isaac prima", "Il gioco carica le mod solo all'avvio."); return true; }
			if (modsDir == null) { Dialog.Show(this, "Non trovo la cartella mods di Isaac", "Indicala in Impostazioni.", DialogKind.Warning); return true; }
			return false;
		}

		void ShowError(string what, Exception e) => Dialog.Show(this, what, Explain(e), DialogKind.Warning);

		// What went wrong in words a player can act on; the system message stays as a detail.
		static string Explain(Exception e)
		{
			if (e is AggregateException a && a.InnerException != null) e = a.InnerException;
			var hint = e is System.Net.Http.HttpRequestException || e is TaskCanceledException ? "Non riesco a raggiungere Internet (GitHub o Steam): controlla la connessione e riprova."
				: e is UnauthorizedAccessException ? "Windows non mi lascia scrivere lì: chiudi i programmi che usano quei file, oppure sposta BTO Sync in una cartella tua (non in Programmi)."
				: e is System.IO.IOException ? "Un file è in uso da un altro programma (Isaac, Steam o una finestra di Esplora file): chiudilo e riprova."
				: null;
			return hint == null ? e.Message : hint + NL + "Dettaglio: " + e.Message;
		}

		// tie: also bring the BTO mods in line with the Workshop mods (not when comparing: there the host's list wins)
		async Task Reload(bool tie = true)
		{
			if (modsDir == null) { ModsDirText.Text = "Non trovata: premi Cambia e scegli la cartella mods di Isaac."; ModsSummary.Text = "Cartella mods non trovata: impostala in Impostazioni."; return; }
			ModsDirText.Text = modsDir;
			Status("Leggo le mod…");
			try { local = await Task.Run(() => Core.ScanMods(modsDir)); }
			catch (Exception e) { ShowError("Non riesco a leggere la cartella mods.", e); return; }
			var open = new HashSet<string>(mods.Where(m => m.IsExpanded).Select(m => m.Key));
			mods.Clear();
			foreach (var m in local) { m.Unlocked = !isaac && !busy; m.IsExpanded = open.Contains(m.Key); mods.Add(m); }
			try
			{
				var latest = await Core.LatestManifests(local.Where(m => m.Id != "").Select(m => m.Id).ToList());
				foreach (var m in local) m.UpdateStatus(latest);
				Status("Pronto.");
			}
			catch { foreach (var m in local) m.UpdateStatus(new Dictionary<string, string>()); Status("Workshop non raggiungibile: non posso controllare gli aggiornamenti."); }
			foreach (var w in ws) w.Installed = Core.HasWorkshopCopy(w.Id.ToString(), modsDir);
			RefreshSummary();
			if (tie) await Tie();
			else if (assets != null) ShowMissingBase(Core.MissingBase(local, Core.Catalog(assets)));  // an alignment may have installed some
		}

		Dictionary<string, string> assets;  // GitHub release assets, read once per session

		async Task<Dictionary<string, string>> Assets() => assets ?? (assets = await Core.BtoAssets());

		// Installs, enables or disables the BTO fixes so they follow the Workshop mods they fix (Core.TiePlan).
		async Task Tie()
		{
			if (modsDir == null) return;
			Dictionary<string, (string version, string url)> catalog;
			try { catalog = Core.Catalog(await Assets()); }
			catch { catalog = local.Where(m => m.Managed).ToDictionary(m => m.Folder, m => (m.Version, (string)null)); }  // offline: on/off only
			ShowMissingBase(Core.MissingBase(local, catalog));
			if (busy || Core.IsaacRunning || Core.Screenshots) return;  // never write mods while only pretending the game is closed
			var todo = Core.TiePlan(local, catalog).Actions;
			if (todo.Count == 0) return;
			busy = true; UpdateState();
			var warnings = await Run(todo);
			busy = false; UpdateState();
			await Reload(false);
			Status(warnings.Count > 0 ? "Mod BTO: " + string.Join(" · ", warnings) : "Mod BTO allineate alle tue mod: " + string.Join(" · ", todo.Select(a => a.Text)) + ".");
		}

		// Runs alignment actions in order; returns what went wrong, one line per action.
		async Task<List<string>> Run(IEnumerable<SyncAction> actions)
		{
			var warnings = new List<string>();
			foreach (var a in actions)
			{
				a.State = "…";
				try
				{
					switch (a.Kind)
					{
						case ActionKind.Enable: Core.SetEnabled(a.Local, true); break;
						case ActionKind.Disable: Core.SetEnabled(a.Local, false); break;
						case ActionKind.Download:
							if (!(await Assets()).TryGetValue($"{a.Host.folder}-{a.Host.version}.zip", out var url))
								throw new Exception("non c'è nelle release di GitHub: chiedi la cartella all'host");
							var w = await Core.InstallBto(a.Host, url, modsDir);
							if (w != null) warnings.Add(a.Host.name + ": " + w);
							break;
						case ActionKind.Subscribe:
							var err = Steam.Connect();
							UpdateState();
							if (err != null) { Process.Start("steam://url/CommunityFilePage/" + a.Host.id); throw new Exception("apri Steam e premi Iscriviti nella pagina appena aperta (" + err + ")"); }
							await Steam.SubscribeAndInstall(ulong.Parse(a.Host.id), modsDir, p => Dispatcher.BeginInvoke(new Action(() => a.State = $"scarico {p:P0}")));
							break;
					}
					a.State = "fatto";
				}
				catch (Exception ex) { a.State = "errore"; warnings.Add(a.Text + ": " + ex.Message); }
			}
			return warnings;
		}

		void RefreshSummary()
		{
			int upd = local.Count(m => m.CanUpdate);
			ModsSummary.Text = $"{local.Count} installate · {local.Count(m => m.Enabled)} attive" + (upd > 0 ? $" · {upd} da aggiornare" : "");
			UpdateAllBtn.Visibility = upd > 0 ? Visibility.Visible : Visibility.Collapsed;
			UpdateAllText.Text = upd == 1 ? "Aggiorna 1 mod" : $"Aggiorna {upd} mod";
			CollectionViewSource.GetDefaultView(mods).Refresh();
			RefreshProfiles();
		}

		// ---------- navigation ----------
		void Nav_Checked(object sender, RoutedEventArgs e)
		{
			if (PageMods == null) return;  // during InitializeComponent
			PageMods.Visibility = NavMods.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
			PageWs.Visibility = NavWs.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
			PageOnline.Visibility = NavOnline.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
			PageProfiles.Visibility = NavProfiles.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
			PageSaves.Visibility = NavSaves.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
			PageSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
			PageInfo.Visibility = NavInfo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
			if (NavWs.IsChecked == true && !wsOpened) { wsOpened = true; _ = Search(true); }
			if (NavSaves.IsChecked == true) RefreshSaves();
		}

		void ProfileCard_Click(object sender, MouseButtonEventArgs e) => NavProfiles.IsChecked = true;

		void Launch_Click(object sender, RoutedEventArgs e)
		{
			Steam.Disconnect();
			UpdateState();
			Process.Start("steam://rungameid/" + Core.AppId);
			Status("Avvio Isaac tramite Steam…");
		}

		void Disconnect_Click(object sender, RoutedEventArgs e) { Steam.Disconnect(); UpdateState(); Status("Scollegato da Steam."); }

		// ---------- installed mods ----------
		bool ModFilter(object o)
		{
			var m = (LocalMod)o;
			var q = ModsSearch?.Text?.Trim() ?? "";
			if (q != "" && m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && m.Folder.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) return false;
			if (FilterOn?.IsChecked == true) return m.Enabled;
			if (FilterOff?.IsChecked == true) return !m.Enabled;
			if (FilterUpd?.IsChecked == true) return m.CanUpdate;
			return true;
		}

		void ModsSearch_TextChanged(object sender, TextChangedEventArgs e) => CollectionViewSource.GetDefaultView(mods).Refresh();
		void Filter_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) CollectionViewSource.GetDefaultView(mods).Refresh(); }
		async void Refresh_Click(object sender, RoutedEventArgs e) => await Reload();
		void OpenDir_Click(object sender, RoutedEventArgs e) { if (modsDir != null) Process.Start("explorer.exe", "\"" + modsDir + "\""); }
		void ModRow_Click(object sender, MouseButtonEventArgs e) { var m = Ctx<LocalMod>(sender); m.IsExpanded = !m.IsExpanded; }
		void ModRow_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Space) { ModRow_Click(sender, null); e.Handled = true; } }
		void ModFolder_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", "\"" + Ctx<LocalMod>(sender).Path + "\"");
		void ModPage_Click(object sender, RoutedEventArgs e) => Process.Start("steam://url/CommunityFilePage/" + Ctx<LocalMod>(sender).Id);

		async void Toggle_Click(object sender, RoutedEventArgs e)
		{
			var m = Ctx<LocalMod>(sender);
			try
			{
				if (Core.IsaacRunning) throw new InvalidOperationException("chiudi Isaac prima");
				Core.SetEnabled(m, m.Enabled);
				RefreshSummary();
				Status((m.Enabled ? "Attivata: " : "Disattivata: ") + m.Name);
			}
			catch (Exception ex) { m.Enabled = !m.Enabled; ShowError("Non posso cambiare " + m.Name + ".", ex); return; }
			if (Core.Ties.Values.Any(t => t.id == m.Id)) await Tie();  // its BTO fixes follow it
		}

		async Task<bool> UpdateWorkshopMod(LocalMod m)
		{
			var err = Steam.Connect();
			UpdateState();
			if (err != null) { Dialog.Show(this, "Steam non collegato", err, DialogKind.Warning); return false; }
			try
			{
				m.Status = "Scarico…"; m.CanUpdate = false;
				await Steam.SubscribeAndInstall(ulong.Parse(m.Id), modsDir, p => Dispatcher.BeginInvoke(new Action(() => m.Status = $"Scarico {p:P0}")));
				return true;
			}
			catch (Exception ex) { ShowError("Aggiornamento di " + m.Name + " non riuscito.", ex); return false; }
		}

		async void UpdateMod_Click(object sender, RoutedEventArgs e)
		{
			var m = Ctx<LocalMod>(sender);
			if (Blocked()) return;
			if (await UpdateWorkshopMod(m)) Status("Aggiornata: " + m.Name);
			await Reload();
		}

		async void UpdateAll_Click(object sender, RoutedEventArgs e)
		{
			if (Blocked()) return;
			var todo = local.Where(m => m.CanUpdate).ToList();
			int ok = 0;
			foreach (var m in todo) if (await UpdateWorkshopMod(m)) ok++;
			Status($"Aggiornate {ok} mod su {todo.Count}.");
			await Reload();
		}

		async void ModRemove_Click(object sender, RoutedEventArgs e)
		{
			var m = Ctx<LocalMod>(sender);
			if (Blocked()) return;
			var how = m.Id != "" ? "Ti disiscrivo dal Workshop e la tolgo dalla cartella mods." : "La tolgo dalla cartella mods (puoi riscaricarla da Gioca online).";
			if (!Dialog.Show(this, $"Disinstallare \"{m.Name}\"?", how, DialogKind.Danger, "Disinstalla", "Annulla")) return;
			try
			{
				if (m.Id != "")
				{
					var err = Steam.Connect();
					UpdateState();
					if (err == null) await Steam.Unsubscribe(ulong.Parse(m.Id));
					else Status("Steam non collegato: tolta solo dalla cartella mods, ricordati di disiscriverti dal Workshop.");
				}
				Core.DeleteMod(m);
				Status("Disinstallata: " + m.Name);
			}
			catch (Exception ex) { ShowError("Non riesco a disinstallare " + m.Name + ".", ex); }
			await Reload();
		}

		// ---------- online ----------
		async void Export_Click(object sender, RoutedEventArgs e)
		{
			var on = local.Where(m => m.Enabled).ToList();
			var code = Core.ToCode(on);
			int shared = on.Count(m => !Core.IsPrivate(m.Folder));
			ExportBox.Text = code;  // visible copy even if the clipboard fails
			ExportBox.Visibility = Visibility.Visible;
			bool copied = NativeClipboard.SetText(code);
			ExportResult.Text = copied ? $"Codice copiato ({shared} mod attive): incollalo su Discord agli altri giocatori."
				: "Non riesco a usare gli appunti: seleziona il codice qui sopra e copialo con Ctrl+C.";
			Status(copied ? "Codice dell'host copiato negli appunti." : "Appunti non disponibili.");
			var warn = new List<string>();
			var onlyHere = on.Where(m => m.Id == "" && !Core.IsBto(m.Folder) && !Core.IsPrivate(m.Folder)).Select(m => m.Name).ToList();
			if (onlyHere.Count > 0) warn.Add("Né sul Workshop né su GitHub, gli altri dovranno chiederti la cartella: " + string.Join(", ", onlyHere));
			var links = on.Where(m => m.Link).Select(m => m.Name).ToList();
			if (links.Count > 0) warn.Add("Collegate a una cartella di sviluppo, gli altri riceveranno la versione della release: " + string.Join(", ", links));
			try
			{
				var assets = await Core.BtoAssets();
				var unreleased = on.Where(m => Core.IsBto(m.Folder) && !m.Link && !assets.ContainsKey($"{m.Folder}-{m.Version}.zip")).Select(m => $"{m.Name} {m.Version}").ToList();
				if (unreleased.Count > 0) warn.Add("Non sono in nessuna release su GitHub, gli altri non potranno scaricarle: " + string.Join(", ", unreleased));
			}
			catch { }
			ExportWarn.ItemsSource = warn;
		}

		void Paste_Click(object sender, RoutedEventArgs e)
		{
			try { if (Clipboard.ContainsText()) CodeBox.Text = Clipboard.GetText(); }
			catch (System.Runtime.InteropServices.COMException) { Status("Appunti occupati da un altro programma: incolla con Ctrl+V."); }
		}

		// The comparison runs by itself as soon as the box holds a valid code: no separate "Confronta" step.
		string compared;

		async void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			var text = CodeBox.Text.Trim();
			CodeError.Visibility = Visibility.Collapsed;
			if (text == compared) return;
			compared = null;
			PlanCard.Visibility = Visibility.Collapsed;
			if (text == "") return;
			try { Core.FromCode(text); }
			catch (FormatException ex) { CodeError.Text = ex.Message; CodeError.Visibility = Visibility.Visible; return; }
			compared = text;
			await Compare();
		}

		// A host code already in the clipboard means the user is about to join: open straight on Gioca online.
		void OpenHostCodeFromClipboard()
		{
			try
			{
				var clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
				if (!clip.Contains("BTO1:")) return;
				Core.FromCode(clip);
				NavOnline.IsChecked = true;
				CodeBox.Text = clip;
			}
			catch (Exception) { }  // clipboard busy or not a code: start as usual
		}

		async Task Compare()
		{
			List<HostMod> host;
			try { host = Core.FromCode(CodeBox.Text); }
			catch (FormatException) { return; }  // CodeBox_TextChanged already shows why
			await Reload(false);
			Status("Confronto con l'host…");
			var ids = host.Where(h => !string.IsNullOrEmpty(h.id)).Select(h => h.id).Union(local.Where(l => l.Id != "").Select(l => l.Id)).ToList();
			Dictionary<string, string> latest;
			try { latest = await Core.LatestManifests(ids); } catch { latest = new Dictionary<string, string>(); }
			plan = Core.MakePlan(host, local, latest);
			PlanCard.Visibility = Visibility.Visible;
			NotesList.ItemsSource = plan.Notes;
			PlanList.ItemsSource = plan.Actions;
			ApplyBtn.Visibility = plan.Actions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
			PlanLaunchBtn.Visibility = plan.Actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			if (plan.Actions.Count == 0 && plan.Notes.Count == 0) { PlanTitle.Text = "Sei pronto a giocare"; PlanSub.Text = $"Hai le stesse {host.Count} mod dell'host: avvia Isaac ed entra in partita."; }
			else if (plan.Actions.Count == 0) { PlanTitle.Text = "Quasi pronto"; PlanSub.Text = "Niente da fare in automatico, ma controlla gli avvisi."; }
			else { PlanTitle.Text = plan.Actions.Count == 1 ? "1 cosa da sistemare" : $"{plan.Actions.Count} cose da sistemare"; PlanSub.Text = "Allinea ora le fa tutte. Le mod disattivate restano installate."; }
			Status("Confronto fatto.");
		}

		async void Apply_Click(object sender, RoutedEventArgs e)
		{
			if (plan == null || Blocked()) return;
			var kept = KeepCurrent();
			busy = true; UpdateState();
			var warnings = await Run(plan.Actions);
			busy = false; UpdateState();
			if (warnings.Count > 0) Dialog.Show(this, "Alcune cose non sono andate", string.Join(NL, warnings), DialogKind.Warning);
			await Compare();  // shows what is left
			if (kept != null) Status($"Allineato. Le tue mod di prima sono nel profilo \"{kept}\": da Profili le rimetti con Applica.");
		}

		// ---------- workshop ----------
		int SortIndex => SortSubs.IsChecked == true ? 1 : SortNew.IsChecked == true ? 2 : 0;
		async void More_Click(object sender, RoutedEventArgs e) => await Search(false);
		async void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await Search(true); }
		async void Sort_Checked(object sender, RoutedEventArgs e) { if (IsLoaded && wsOpened) await Search(true); }
		void WsRow_Click(object sender, MouseButtonEventArgs e) { var w = Ctx<WsItem>(sender); w.IsExpanded = !w.IsExpanded; }
		void WsRow_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Space) { WsRow_Click(sender, null); e.Handled = true; } }
		void WsPage_Click(object sender, RoutedEventArgs e) => Process.Start("steam://url/CommunityFilePage/" + Ctx<WsItem>(sender).Id);

		async Task Search(bool reset)
		{
			UpdateState();
			if (isaac) { WsInfo.Text = "Chiudi Isaac per cercare e installare mod dal Workshop."; return; }
			var err = Steam.Connect();
			UpdateState();
			if (err != null) { WsInfo.Text = err; return; }
			if (reset) { ws.Clear(); wsPage = 1; }
			WsInfo.Text = "Cerco…";
			MoreBtn.Visibility = Visibility.Collapsed;
			try
			{
				var (items, total) = await Steam.Search(SearchBox.Text, SortIndex, wsPage);
				foreach (var i in items) ws.Add(WsItem.From(i, modsDir));
				wsPage++;
				WsInfo.Text = total == 0 ? "Nessuna mod trovata." : $"{total:N0} mod trovate · ne vedi {ws.Count} · clicca una mod per la descrizione completa";
				MoreBtn.Visibility = ws.Count < total ? Visibility.Visible : Visibility.Collapsed;
			}
			catch (Exception ex) { WsInfo.Text = "Ricerca non riuscita: " + ex.Message; }
		}

		async void WsAct_Click(object sender, RoutedEventArgs e)
		{
			var w = Ctx<WsItem>(sender);
			if (Blocked()) return;
			var err = Steam.Connect();
			UpdateState();
			if (err != null) { w.Error = err; return; }
			w.Error = "";
			if (w.Installed)
			{
				if (!Dialog.Show(this, $"Disinstallare \"{w.Title}\"?", "Ti disiscrivo dal Workshop e la tolgo dalla cartella mods.", DialogKind.Danger, "Disinstalla", "Annulla")) return;
				w.Busy = true;
				try { await Steam.Unsubscribe(w.Id); Core.RemoveWorkshopCopy(w.Id.ToString(), modsDir); w.Installed = false; Status("Disinstallata: " + w.Title); }
				catch (Exception ex) { w.Error = ex.Message; }
			}
			else
			{
				w.Busy = true; w.Progress = 0;
				try
				{
					await Steam.SubscribeAndInstall(w.Id, modsDir, p => Dispatcher.BeginInvoke(new Action(() => w.Progress = p)));
					w.Installed = true;
					Status("Installata e attiva: " + w.Title);
				}
				catch (Exception ex) { w.Error = ex.Message; }
			}
			w.Busy = false;
			await Reload();
		}

		// ---------- profiles ----------
		void RefreshProfiles()
		{
			foreach (var p in profiles) p.IsActive = p.name == activeProfile;
			ProfilesEmpty.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			var cur = profiles.FirstOrDefault(p => p.IsActive);
			ActiveProfileText.Text = cur?.name ?? "Nessun profilo";
			ActiveProfileSub.Text = cur == null ? "Crea un profilo dalle mod attive"
				: new HashSet<string>(cur.keys).SetEquals(local.Where(m => m.Enabled).Select(m => m.Key)) ? cur.Summary : cur.Summary + " · modificato";
		}

		// Before anything replaces the active mods: if no profile holds them, save them in one, so nothing is lost by mistake.
		// Returns the new profile's name, null when they were already saved.
		string KeepCurrent()
		{
			var cur = ActiveKeys();
			if (cur.Count == 0 || profiles.Any(p => new HashSet<string>(p.keys).SetEquals(cur))) return null;
			return ProfileFromKeys(cur, "Copia automatica " + DateTime.Now.ToString("dd/MM HH:mm"));
		}

		// Adds a profile, with " (2)", " (3)"... when the name is taken. Returns the name used.
		string ProfileFromKeys(List<string> keys, string name)
		{
			var n = name;
			for (int i = 2; profiles.Any(p => p.name == n); i++) n = $"{name} ({i})";
			profiles.Add(new Profile { name = n, keys = keys });
			SaveProfiles();
			return n;
		}

		void SaveHostProfile_Click(object sender, RoutedEventArgs e)
		{
			List<HostMod> host;
			try { host = Core.FromCode(CodeBox.Text); }
			catch (FormatException ex) { Dialog.Show(this, "Codice non valido", ex.Message, DialogKind.Warning); return; }
			var name = ProfileFromKeys(host.Select(h => h.key).ToList(), "Mod dell'host " + DateTime.Now.ToString("dd/MM HH:mm"));
			Status($"Profilo \"{name}\" creato con le {host.Count} mod dell'host: lo trovi in Profili, dove puoi rinominarlo e applicarlo.");
		}

		void SaveProfiles() { try { Core.SaveProfiles(profiles, activeProfile); } catch (Exception ex) { ShowError("Non riesco a salvare i profili.", ex); } RefreshProfiles(); }

		List<string> ActiveKeys() => local.Where(m => m.Enabled).Select(m => m.Key).ToList();

		void NewProfile_Click(object sender, RoutedEventArgs e)
		{
			var name = NewProfileName.Text.Trim();
			if (name == "") name = "Profilo " + (profiles.Count + 1);
			if (profiles.Any(p => p.name == name)) { Dialog.Show(this, "Esiste già un profilo con questo nome", "Scegline un altro.", DialogKind.Warning); return; }
			profiles.Add(new Profile { name = name, keys = ActiveKeys() });
			activeProfile = name;
			NewProfileName.Text = "";
			SaveProfiles();
			Status("Profilo salvato: " + name);
		}

		void ApplyProfile_Click(object sender, RoutedEventArgs e)
		{
			var p = Ctx<Profile>(sender);
			if (Blocked()) return;
			try
			{
				var kept = new HashSet<string>(p.keys).SetEquals(ActiveKeys()) ? null : KeepCurrent();
				var missing = Core.ApplyProfile(p, local);
				activeProfile = p.name;
				SaveProfiles();
				RefreshSummary();
				Status("Profilo applicato: " + p.name + (kept != null ? $". Le mod di prima sono nel profilo \"{kept}\"." : "."));
				if (missing.Count > 0)
					Dialog.Show(this, missing.Count == 1 ? "1 mod del profilo non è installata" : $"{missing.Count} mod del profilo non sono installate", "Per scaricarle fatti mandare il codice del profilo da chi le ha e incollalo in Gioca online.", DialogKind.Warning);
			}
			catch (Exception ex) { ShowError("Non riesco ad applicare il profilo.", ex); }
		}

		void OverwriteProfile_Click(object sender, RoutedEventArgs e)
		{
			var p = Ctx<Profile>(sender);
			p.keys = ActiveKeys();
			activeProfile = p.name;
			SaveProfiles();
			ProfilesList.Items.Refresh();
			Status("Profilo aggiornato con le mod attive: " + p.name);
		}

		void DeleteProfile_Click(object sender, RoutedEventArgs e)
		{
			var p = Ctx<Profile>(sender);
			if (!Dialog.Show(this, $"Eliminare il profilo \"{p.name}\"?", "Le mod restano installate.", DialogKind.Danger, "Elimina", "Annulla")) return;
			profiles.Remove(p);
			if (activeProfile == p.name) activeProfile = null;
			SaveProfiles();
		}

		void ExportProfile_Click(object sender, RoutedEventArgs e)
		{
			var p = Ctx<Profile>(sender);
			var keys = new HashSet<string>(p.keys);
			var code = Core.ToCode(local.Where(m => keys.Contains(m.Key)));
			Status(NativeClipboard.SetText(code) ? $"Codice del profilo \"{p.name}\" copiato negli appunti." : "Appunti non disponibili.");
		}

		void ProfileName_LostFocus(object sender, RoutedEventArgs e)
		{
			var p = Ctx<Profile>(sender);
			if (string.IsNullOrWhiteSpace(p.name)) p.name = "Profilo";
			if (profiles.Any(o => o != p && o.name == p.name)) p.name += " (2)";
			if (profiles.Count(o => o.IsActive) == 1 && profiles.First(o => o.IsActive) == p) activeProfile = p.name;
			SaveProfiles();
		}

		// Importing a code = a profile with the host's mods + the alignment that installs what is missing.
		void ImportProfile_Click(object sender, RoutedEventArgs e)
		{
			List<HostMod> host;
			try { host = Core.FromCode(ImportBox.Text); }
			catch (FormatException ex) { Dialog.Show(this, "Codice non valido", ex.Message, DialogKind.Warning); return; }
			var name = ProfileFromKeys(host.Select(h => h.key).ToList(), "Importato " + DateTime.Now.ToString("dd/MM HH:mm"));
			var code = ImportBox.Text.Trim();
			ImportBox.Text = "";
			NavOnline.IsChecked = true;
			compared = null;
			if (CodeBox.Text.Trim() == code) _ = Compare();
			else CodeBox.Text = code;  // CodeBox_TextChanged compares
			Status($"Profilo \"{name}\" creato: per attivarlo e scaricare ciò che manca usa Allinea ora.");
		}

		// ---------- saves ----------
		List<SaveSlot> slots = new List<SaveSlot>();

		void RefreshSaves()
		{
			try
			{
				Saves.Init(modsDir);
				slots = Saves.Slots();
				var backups = Saves.Backups();
				if (!slots.Any(x => x.Slot == openSlot && x.Exists)) openSlot = 0;
				foreach (var x in slots) x.IsOpen = x.Slot == openSlot;
				SlotsList.ItemsSource = slots;
				ShowProgress();
				BackupsList.ItemsSource = backups;
				BackupsEmpty.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
				int game = backups.Count(b => b.FromGame);
				BackupsCount.Text = backups.Count == 0 ? "" : $"{backups.Count - game} di BTO Sync · {game} copie automatiche del gioco";
				// the Steam Cloud path holds the Steam account number: keep it out of screenshots, show it on hover
				SavesDirText.Text = Saves.SaveDir.IndexOf("userdata", StringComparison.OrdinalIgnoreCase) >= 0 ? "Salvataggi del gioco: in Steam Cloud" : "Salvataggi del gioco: in Documenti, My Games";
				SavesDirText.ToolTip = Saves.SaveDir;
			}
			catch (Exception ex) { ShowError("Non riesco a leggere i salvataggi.", ex); }
		}

		// ---------- progress of a slot (completion marks) ----------
		int openSlot;      // 0 = none, the backup list is shown
		string pickedBoss; // its characters are listed under the tree

		void Slot_Click(object sender, MouseButtonEventArgs e) => ToggleSlot(Ctx<SaveSlot>(sender));
		void Slot_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Space) { ToggleSlot(Ctx<SaveSlot>(sender)); e.Handled = true; } }
		void CloseProgress_Click(object sender, RoutedEventArgs e) { openSlot = 0; RefreshSaves(); }

		void ToggleSlot(SaveSlot s)
		{
			if (!s.Exists) { Status($"Lo {s.Title.ToLower()} è vuoto: non c'è un percorso da mostrare."); return; }
			openSlot = openSlot == s.Slot ? 0 : s.Slot;
			pickedBoss = null;
			RefreshSaves();
		}

		// Where each boss sits in the tree (x, y of the node's top-left corner) and what leads to it.
		static readonly (string key, double x, double y)[] TreeLayout =
		{
			("heart", 0, 108), ("bossrush", 205, 0), ("hush", 410, 0), ("delirium", 615, 0),
			("isaac", 205, 72), ("bluebaby", 410, 72), ("satan", 205, 144), ("lamb", 410, 144), ("megasatan", 615, 108),
			("mother", 205, 236), ("beast", 410, 236), ("greed", 615, 236),
		};
		static readonly (string from, string to)[] TreeEdges =
		{
			("heart", "bossrush"), ("bossrush", "hush"), ("hush", "delirium"), ("heart", "isaac"), ("isaac", "bluebaby"),
			("heart", "satan"), ("satan", "lamb"), ("bluebaby", "megasatan"), ("lamb", "megasatan"),
		};
		const double NodeW = 150, NodeH = 54;

		Brush Res(string key) => (Brush)FindResource(key);

		void ShowProgress()
		{
			var open = slots.FirstOrDefault(x => x.Slot == openSlot);
			var vis = open != null ? Visibility.Visible : Visibility.Collapsed;
			var hide = open != null ? Visibility.Collapsed : Visibility.Visible;
			ProgressPanel.Visibility = vis;
			BackupsHeader.Visibility = BackupsList.Visibility = hide;
			if (open == null) { BackupsEmpty.Visibility = Visibility.Collapsed; return; }
			BackupsEmpty.Visibility = Visibility.Collapsed;

			var bosses = Progress.Of(open.Info);
			ProgressTitle.Text = "Percorso dello " + open.Title.ToLower();
			ProgressTree.Children.Clear();
			var pos = TreeLayout.ToDictionary(t => t.key, t => new Point(t.x, t.y));
			foreach (var (from, to) in TreeEdges)  // elbow lines, lit when both bosses are beaten
			{
				Point a = pos[from], b = pos[to];
				double y1 = a.Y + NodeH / 2, y2 = b.Y + NodeH / 2, x1 = a.X + NodeW, x2 = b.X, xm = x2 - 25;
				ProgressTree.Children.Add(new System.Windows.Shapes.Polyline
				{
					Points = new PointCollection { new Point(x1, y1), new Point(xm, y1), new Point(xm, y2), new Point(x2, y2) },
					Stroke = bosses[from].State > 0 && bosses[to].State > 0 ? Res("Body") : Res("Line"), StrokeThickness = 2,
				});
			}
			var lane = new TextBlock { Text = "Altre strade", Style = (Style)FindResource("Sub"), FontSize = 12 };
			Canvas.SetLeft(lane, 0); Canvas.SetTop(lane, 236 + NodeH / 2 - 9);
			ProgressTree.Children.Add(lane);
			foreach (var (key, x, y) in TreeLayout)
			{
				var node = BossNode(bosses[key]);
				Canvas.SetLeft(node, x); Canvas.SetTop(node, y);
				ProgressTree.Children.Add(node);
			}

			CharsGrid.Children.Clear(); TaintedGrid.Children.Clear();
			if (!open.Info.Played) ProgressHint.Text = "Questo slot non è mai stato giocato: il percorso è tutto da fare.";
			if (pickedBoss == null || !bosses.ContainsKey(pickedBoss))
			{
				CharsBox.Visibility = Visibility.Collapsed;
				if (open.Info.Played)
				{
					int done = bosses.Values.Count(b => b.State > 0);
					ProgressHint.Text = $"Hai battuto {done} boss finali su {bosses.Count}. Clicca un boss per vedere con quali personaggi.";
				}
				return;
			}
			var picked = bosses[pickedBoss];
			int hard = picked.Marks.Count(Progress.IsHard);
			ProgressHint.Text = picked.Beaten == 0 ? $"{picked.Name}: non l'hai ancora battuto con nessun personaggio."
				: $"{picked.Name}: battuto con {picked.Beaten} personaggi su 34" + (hard > 0 ? $", {hard} in difficile." : ".");
			CharsBox.Visibility = Visibility.Visible;
			for (int i = 0; i < Progress.Characters.Length; i++)
				(i < 17 ? CharsGrid : TaintedGrid).Children.Add(CharChip(Progress.Characters[i], picked.Marks[i]));
		}

		// 0 not beaten: dark with an outline; beaten: light; beaten on hard: red, like the game's hard marks.
		(Brush bg, Brush fg, Brush hover) MarkColors(int state) => state == 2 ? (Res("Accent"), Brushes.White, Res("AccentHover"))
			: state == 1 ? (Res("Body"), Res("Bg"), (Brush)new BrushConverter().ConvertFrom("#D7DBE5")) : (Res("Bg"), Res("Muted"), Res("Hover"));

		Button BossNode(BossProgress b)
		{
			var (bg, fg, hover) = MarkColors(b.State);
			var text = new StackPanel { Width = NodeW - 24 };  // full width: the button template centers its content
			text.Children.Add(new TextBlock { Text = b.Name, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
			text.Children.Add(new TextBlock { Text = $"{b.Beaten}/34" + (b.Hard ? " · difficile" : ""), FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 1, 0, 0) });
			var node = new Button
			{
				Style = (Style)FindResource("Btn"), Width = NodeW, Height = NodeH, Padding = new Thickness(12, 6, 12, 6),
				Background = bg, Foreground = fg, Tag = hover, Content = text, HorizontalContentAlignment = HorizontalAlignment.Left,
				BorderBrush = b.Key == pickedBoss ? (Brush)new BrushConverter().ConvertFrom("#7FB0FF") : b.State == 0 ? Res("Line") : bg,
				ToolTip = "Clicca per vedere con quali personaggi",
			};
			System.Windows.Automation.AutomationProperties.SetName(node, $"{b.Name}: battuto con {b.Beaten} personaggi su 34");
			node.Click += (s, e) => { pickedBoss = pickedBoss == b.Key ? null : b.Key; ShowProgress(); };
			return node;
		}

		Border CharChip(string name, int mark)
		{
			var (bg, fg, _) = MarkColors(mark == 0 ? 0 : Progress.IsHard(mark) ? 2 : 1);
			return new Border
			{
				Width = 150, Height = 32, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 8), Background = bg,
				BorderBrush = mark == 0 ? Res("Line") : bg, BorderThickness = new Thickness(1), Padding = new Thickness(10, 0, 10, 0),
				Child = new TextBlock { Text = name, Foreground = fg, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
			};
		}

		static readonly string NL = Environment.NewLine + Environment.NewLine;
		// text = "Question?" + NL + details; ok names the action ("Ripristina", "Scarica"...)
		bool Ask(string text, string ok = "Sì", bool danger = false)
		{
			int i = text.IndexOf(NL, StringComparison.Ordinal);
			return Dialog.Show(this, i < 0 ? text : text.Substring(0, i), i < 0 ? null : text.Substring(i + NL.Length), danger ? DialogKind.Danger : DialogKind.Question, ok, "Annulla");
		}

		void SaveAction(string done, Action act)
		{
			UpdateState();
			if (isaac) { Dialog.Show(this, "Chiudi Isaac prima", "Mentre è aperto, il gioco riscrive i salvataggi."); return; }
			try { act(); Status(done); }
			catch (Exception ex) { ShowError("Operazione sui salvataggi non riuscita.", ex); }
			RefreshSaves();
		}

		// Small menu under the button to choose the target slot.
		void PickSlot(object button, int except, Action<SaveSlot> pick)
		{
			var menu = new ContextMenu { PlacementTarget = (UIElement)button, Placement = PlacementMode.Bottom };
			foreach (var s in slots.Where(s => s.Slot != except))
			{
				var item = new MenuItem { Header = $"{s.Title}  ·  {s.Short}" };
				item.Click += (o, e) => pick(s);
				menu.Items.Add(item);
			}
			menu.IsOpen = true;
		}

		static string Overwrites(SaveSlot s) => s.Exists ? $"Lo {s.Title.ToLower()} ({s.Short}) viene sostituito: prima ne salvo un backup." : $"Lo {s.Title.ToLower()} è vuoto.";

		void SlotBackup_Click(object sender, RoutedEventArgs e)
		{
			var s = Ctx<SaveSlot>(sender);
			SaveAction($"Backup dello {s.Title.ToLower()} salvato.", () => Saves.Backup(s.Slot, "Backup manuale"));
		}

		void SlotCopy_Click(object sender, RoutedEventArgs e)
		{
			var s = Ctx<SaveSlot>(sender);
			PickSlot(sender, s.Slot, to =>
			{
				if (Ask($"Copiare lo {s.Title.ToLower()} nello {to.Title.ToLower()}?{NL}{Overwrites(to)}", "Copia"))
					SaveAction($"{s.Title} copiato nello {to.Title.ToLower()}.", () => Saves.Copy(s.Slot, to.Slot));
			});
		}

		void BackupRestore_Click(object sender, RoutedEventArgs e)
		{
			var b = Ctx<SaveBackup>(sender);
			PickSlot(sender, 0, to =>
			{
				if (Ask($"Ripristinare il backup \"{b.Title}\" nello {to.Title.ToLower()}?{NL}{b.Sub}{NL}{Overwrites(to)}", "Ripristina"))
					SaveAction($"Backup ripristinato nello {to.Title.ToLower()}.", () => Saves.Restore(b, to.Slot));
			});
		}

		// Export reads the slot only: allowed while Isaac runs (the file may then be a few minutes old).
		void SlotExport_Click(object sender, RoutedEventArgs e)
		{
			var s = Ctx<SaveSlot>(sender);
			var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Esporta lo " + s.Title.ToLower(), Filter = "Salvataggio esportato (*.zip)|*.zip", FileName = $"Isaac slot {s.Slot} {DateTime.Now:yyyy-MM-dd}.zip" };
			if (dlg.ShowDialog(this) != true) return;
			try { Saves.Export(s.Slot, dlg.FileName); Status($"{s.Title} esportato in {dlg.FileName}: puoi tenerlo o mandarlo a un amico, che lo apre con Importa."); }
			catch (Exception ex) { ShowError("Esportazione non riuscita.", ex); }
		}

		// Import only adds a backup: nothing is overwritten until the user restores it into a slot.
		void ImportSave_Click(object sender, RoutedEventArgs e)
		{
			var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Importa un salvataggio", Filter = "Salvataggio (*.zip, *.dat)|*.zip;*.dat|Tutti i file|*.*" };
			if (dlg.ShowDialog(this) != true) return;
			try { Saves.Import(dlg.FileName); openSlot = 0; RefreshSaves(); Status("Salvataggio importato: è in cima ai backup, usa \"Ripristina in...\" per metterlo in uno slot."); }
			catch (Exception ex) { ShowError("Non riesco a importare questo file.", ex); }
		}

		void BackupDelete_Click(object sender, RoutedEventArgs e)
		{
			var b = Ctx<SaveBackup>(sender);
			if (Ask($"Eliminare il backup \"{b.Title}\"?{NL}{b.Sub}", "Elimina", true))
				SaveAction("Backup eliminato.", () => Saves.Delete(b));
		}

		void OpenBackups_Click(object sender, RoutedEventArgs e)
		{
			System.IO.Directory.CreateDirectory(Saves.BackupDir);
			Process.Start("explorer.exe", "\"" + Saves.BackupDir + "\"");
		}

		// ---------- settings ----------
		async void ChangeDir_Click(object sender, RoutedEventArgs e)
		{
			using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Cartella mods di Isaac (dentro la cartella del gioco)", SelectedPath = modsDir ?? "" })
			{
				if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
				modsDir = d.SelectedPath;
				Core.SaveModsDir(modsDir);
			}
			await Reload();
		}

		void GitHub_Click(object sender, RoutedEventArgs e) => Process.Start("https://github.com/" + Core.Repo);
		void InfoDoc_Click(object sender, RoutedEventArgs e) => Process.Start($"https://github.com/{Core.Repo}/blob/master/docs/BTO-Sync.md");
		void InfoSource_Click(object sender, RoutedEventArgs e) => Process.Start($"https://github.com/{Core.Repo}/tree/master/sync");

		// ---------- online patch ----------
		string GameExe => modsDir == null ? null : System.IO.Path.Combine(System.IO.Directory.GetParent(modsDir).FullName, "isaac-ng.exe");

		// Right after the patch: the base-game fixes are the other half of playing online, offer them now.
		async void AfterPatcher()
		{
			CheckPatch();
			if (missingBase.Count > 0 && Ask($"Scaricare anche i fix BTO del gioco base?{NL}{string.Join(", ", missingBase.Select(h => "BTO " + Pretty(h.folder)))}{NL}Evitano le cause di desync del gioco base trovate nelle nostre partite. Li scarico da GitHub e li attivo; poi li tengo aggiornati io.", "Scarica"))
				await InstallBase();
		}

		async void CheckPatch()
		{
			var exe = GameExe;
			var st = exe == null ? Core.PatchState.NoExe : await Task.Run(() => Core.OnlinePatch(exe));
			PatchText.Text = st == Core.PatchState.Present ? "Presente: puoi giocare online con le mod attive. Dopo un aggiornamento di Isaac va rifatta."
				: st == Core.PatchState.Absent ? "Manca: senza, Isaac ti fa entrare online solo con le mod spente."
				: st == Core.PatchState.Unknown ? "Non riconosco questo isaac-ng.exe (il gioco si è aggiornato?): la patch potrebbe mancare."
				: "Non trovo isaac-ng.exe accanto alla cartella mods.";
			PatchBanner.Visibility = st == Core.PatchState.Absent || st == Core.PatchState.Unknown ? Visibility.Visible : Visibility.Collapsed;
			PatchBannerText.Text = st == Core.PatchState.Absent ? "Manca la patch per giocare online con le mod: senza, Isaac ti fa entrare online solo con le mod spente."
				: "Non riconosco questo isaac-ng.exe (il gioco si è aggiornato?): la patch per giocare online con le mod potrebbe mancare.";
		}

		async void Patch_Click(object sender, RoutedEventArgs e)
		{
			if (Blocked()) return;
			if (!Ask($"Scaricare la patch per l'online?{NL}Scarico Isaac-Online-Modded di xADDBx dalla sua pagina GitHub ({Core.PatcherRepo}) e lo apro: è il programma che modifica isaac-ng.exe per lasciarti entrare online con le mod attive. Poi segui le sue istruzioni.{NL}Se Windows chiede di installare .NET, accetta: gli serve per partire.", "Scarica")) return;
			busy = true; UpdateState();
			Status("Scarico Isaac-Online-Modded…");
			try
			{
				var p = Process.Start(await Core.DownloadPatcher());
				Status("Isaac-Online-Modded è aperto: quando lo chiudi ricontrollo la patch.");
				if (p != null) { p.EnableRaisingEvents = true; p.Exited += (s, a) => Dispatcher.BeginInvoke(new Action(AfterPatcher)); }
			}
			catch (Exception ex) { Status("Patch non scaricata."); ShowError("Non riesco a scaricare o aprire Isaac-Online-Modded.", ex); }
			busy = false; UpdateState();
		}

		// ---------- base-game fixes: proposed, right under the patch notice ----------
		List<HostMod> missingBase = new List<HostMod>();
		bool baseLater;  // "Non ora": hidden until the next start

		static string Pretty(string folder) => string.Join(" ", folder.Substring(4).Split('-').Select(w => char.ToUpper(w[0]) + w.Substring(1)));

		void ShowMissingBase(List<HostMod> missing)
		{
			missingBase = missing;
			BaseBanner.Visibility = missing.Count > 0 && !baseLater ? Visibility.Visible : Visibility.Collapsed;
			BaseBannerText.Text = (missing.Count == 1 ? "Manca 1 fix BTO del gioco base" : $"Mancano {missing.Count} fix BTO del gioco base")
				+ $" ({string.Join(", ", missing.Select(h => Pretty(h.folder)))}): senza, online la partita può dividersi.";
		}

		void BaseLater_Click(object sender, RoutedEventArgs e) { baseLater = true; BaseBanner.Visibility = Visibility.Collapsed; }

		async void InstallBase_Click(object sender, RoutedEventArgs e) => await InstallBase();

		async Task InstallBase()
		{
			if (missingBase.Count == 0 || Blocked()) return;
			busy = true; UpdateState();
			Status("Scarico i fix BTO del gioco base…");
			var todo = missingBase.Select(h => new SyncAction { Kind = ActionKind.Download, Host = new HostMod { key = h.key, folder = h.folder, name = Pretty(h.folder), version = h.version }, Text = "BTO " + Pretty(h.folder) }).ToList();
			var warnings = await Run(todo);
			busy = false; UpdateState();
			if (warnings.Count > 0) Dialog.Show(this, "Alcuni fix non sono stati scaricati", string.Join(NL, warnings), DialogKind.Warning);
			await Reload();
			if (warnings.Count == 0) Status($"Installati e attivi: {string.Join(", ", todo.Select(a => a.Text))}.");
		}

		// ---------- self-update ----------
		AppRelease update;

		async Task CheckUpdate()
		{
			UpdateText.Text = "Controllo gli aggiornamenti su GitHub…";
			try
			{
				var r = await Updater.Latest();
				update = r != null && r.Version > Updater.Current ? r : null;
				UpdateText.Text = update != null ? $"È disponibile la versione {update.VersionText}: premi Aggiorna."
					: r == null ? "Nessuna versione pubblicata su GitHub." : "Hai l'ultima versione.";
			}
			catch (Exception ex) { update = null; UpdateText.Text = "Non riesco a controllare gli aggiornamenti: " + ex.Message; }
			var vis = update != null ? Visibility.Visible : Visibility.Collapsed;
			UpdateBanner.Visibility = UpdateNowBtn.Visibility = vis;
			if (update == null) return;
			UpdateBannerText.Text = $"È disponibile BTO Sync {update.VersionText} (hai la {Updater.Current.ToString(3)}).";
			UpdateNowText.Text = "Aggiorna a " + update.VersionText;
		}

		async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckUpdate();
		void UpdateNotes_Click(object sender, RoutedEventArgs e) { if (update?.Page != null) Process.Start(update.Page); }

		async void Update_Click(object sender, RoutedEventArgs e)
		{
			if (update == null) return;
			if (busy) { Dialog.Show(this, "Aspetta che finisca l'operazione in corso"); return; }
			var notes = update.Notes.Replace("**", "").Trim();
			if (notes.Length > 700) notes = notes.Substring(0, 700) + "…";
			if (!Ask($"Aggiornare BTO Sync alla versione {update.VersionText}?{NL}{notes}{NL}Scarico l'aggiornamento da GitHub, sostituisco i file del programma e lo riavvio. Profili e backup restano.", "Aggiorna")) return;
			busy = true; UpdateState();
			Status($"Scarico BTO Sync {update.VersionText}…");
			try
			{
				var zip = await Updater.Download(update);
				await Task.Run(() => Updater.Apply(zip, Updater.AppDir));
				try { System.IO.File.Delete(zip); } catch { }
				Steam.Disconnect();
				Updater.Restart();
				Application.Current.Shutdown();
			}
			catch (Exception ex)
			{
				busy = false; UpdateState();
				Status("Aggiornamento non riuscito.");
				ShowError("Aggiornamento non riuscito: il programma è rimasto alla versione di prima.", ex);
			}
		}
	}

	// Plain Win32 clipboard: WPF/WinForms go through OLE, which fails with CLIPBRD_E_CANT_OPEN on some PCs (seen on the dev PC).
	static class NativeClipboard
	{
		[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr owner);
		[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool CloseClipboard();
		[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EmptyClipboard();
		[System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr mem);

		public static bool SetText(string s)
		{
			for (int i = 0; !OpenClipboard(IntPtr.Zero); i++)
			{
				if (i == 40) return false;
				System.Threading.Thread.Sleep(25);
			}
			try
			{
				EmptyClipboard();
				var mem = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(s);
				if (SetClipboardData(13 /* CF_UNICODETEXT */, mem) != IntPtr.Zero) return true;  // the system owns mem now
				System.Runtime.InteropServices.Marshal.FreeHGlobal(mem);
				return false;
			}
			finally { CloseClipboard(); }
		}
	}
}
