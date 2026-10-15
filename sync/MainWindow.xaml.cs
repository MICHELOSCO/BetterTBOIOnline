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
			modsDir = Core.FindModsDir();
			var (list, active) = Core.LoadProfiles();
			foreach (var p in list) profiles.Add(p);
			activeProfile = active;
			var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
			timer.Tick += (s, e) => UpdateState();
			timer.Start();
			RefreshProfiles();
			Loaded += async (s, e) => { UpdateState(); await Reload(); };
			Closing += (s, e) => Steam.Disconnect();
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
			SteamBox.Visibility = Steam.Ready ? Visibility.Visible : Visibility.Collapsed;
			foreach (var m in mods) m.Unlocked = !isaac && !busy;
			ApplyBtn.IsEnabled = !isaac && !busy;
			LaunchBtn.IsEnabled = !isaac;
		}

		bool Blocked()
		{
			UpdateState();
			if (isaac) { MessageBox.Show(this, "Chiudi Isaac prima: il gioco carica le mod solo all'avvio.", "BTO Sync", MessageBoxButton.OK, MessageBoxImage.Information); return true; }
			if (modsDir == null) { MessageBox.Show(this, "Non trovo la cartella mods di Isaac: indicala in Impostazioni.", "BTO Sync"); return true; }
			return false;
		}

		void ShowError(string what, Exception e) => MessageBox.Show(this, what + "\n\n" + e.Message, "BTO Sync", MessageBoxButton.OK, MessageBoxImage.Warning);

		async Task Reload()
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
		void ModFolder_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", "\"" + Ctx<LocalMod>(sender).Path + "\"");
		void ModPage_Click(object sender, RoutedEventArgs e) => Process.Start("steam://url/CommunityFilePage/" + Ctx<LocalMod>(sender).Id);

		void Toggle_Click(object sender, RoutedEventArgs e)
		{
			var m = Ctx<LocalMod>(sender);
			try
			{
				if (Core.IsaacRunning) throw new InvalidOperationException("chiudi Isaac prima");
				Core.SetEnabled(m, m.Enabled);
				RefreshSummary();
				Status((m.Enabled ? "Attivata: " : "Disattivata: ") + m.Name);
			}
			catch (Exception ex) { m.Enabled = !m.Enabled; ShowError("Non posso cambiare " + m.Name + ".", ex); }
		}

		async Task<bool> UpdateWorkshopMod(LocalMod m)
		{
			var err = Steam.Connect();
			UpdateState();
			if (err != null) { MessageBox.Show(this, err, "BTO Sync"); return false; }
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
			if (MessageBox.Show(this, $"Disinstallare \"{m.Name}\"?\n\n{how}", "BTO Sync", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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
			ExportBox.Text = code;  // visible copy even if the clipboard fails
			ExportBox.Visibility = Visibility.Visible;
			bool copied = NativeClipboard.SetText(code);
			ExportResult.Text = copied ? $"Codice copiato ({on.Count} mod attive): incollalo su Discord agli altri giocatori."
				: "Non riesco a usare gli appunti: seleziona il codice qui sopra e copialo con Ctrl+C.";
			Status(copied ? "Codice dell'host copiato negli appunti." : "Appunti non disponibili.");
			var warn = new List<string>();
			var onlyHere = on.Where(m => m.Id == "" && !Core.IsBto(m.Folder)).Select(m => m.Name).ToList();
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

		async void Compare_Click(object sender, RoutedEventArgs e) => await Compare();

		async Task Compare()
		{
			List<HostMod> host;
			try { host = Core.FromCode(CodeBox.Text); }
			catch (FormatException ex) { MessageBox.Show(this, ex.Message, "Codice non valido", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
			await Reload();
			Status("Confronto con l'host…");
			var ids = host.Where(h => !string.IsNullOrEmpty(h.id)).Select(h => h.id).Union(local.Where(l => l.Id != "").Select(l => l.Id)).ToList();
			Dictionary<string, string> latest;
			try { latest = await Core.LatestManifests(ids); } catch { latest = new Dictionary<string, string>(); }
			plan = Core.MakePlan(host, local, latest);
			PlanCard.Visibility = Visibility.Visible;
			NotesList.ItemsSource = plan.Notes;
			PlanList.ItemsSource = plan.Actions;
			ApplyBtn.Visibility = plan.Actions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
			if (plan.Actions.Count == 0 && plan.Notes.Count == 0) { PlanTitle.Text = "Sei allineato all'host"; PlanSub.Text = $"Hai le stesse {host.Count} mod dell'host: puoi entrare in partita."; }
			else if (plan.Actions.Count == 0) { PlanTitle.Text = "Quasi pronto"; PlanSub.Text = "Niente da fare in automatico, ma controlla gli avvisi."; }
			else { PlanTitle.Text = plan.Actions.Count == 1 ? "1 cosa da sistemare" : $"{plan.Actions.Count} cose da sistemare"; PlanSub.Text = "Allinea ora le fa tutte. Le mod disattivate restano installate."; }
			Status("Confronto fatto.");
		}

		async void Apply_Click(object sender, RoutedEventArgs e)
		{
			if (plan == null || Blocked()) return;
			busy = true; UpdateState();
			Dictionary<string, string> assets = null;
			var warnings = new List<string>();
			foreach (var a in plan.Actions)
			{
				a.State = "…";
				try
				{
					switch (a.Kind)
					{
						case ActionKind.Enable: Core.SetEnabled(a.Local, true); break;
						case ActionKind.Disable: Core.SetEnabled(a.Local, false); break;
						case ActionKind.Download:
							if (assets == null) assets = await Core.BtoAssets();
							if (!assets.TryGetValue($"{a.Host.folder}-{a.Host.version}.zip", out var url))
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
			busy = false; UpdateState();
			if (warnings.Count > 0) MessageBox.Show(this, string.Join("\n\n", warnings), "Alcune cose non sono andate", MessageBoxButton.OK, MessageBoxImage.Warning);
			await Compare();  // shows what is left
		}

		// ---------- workshop ----------
		int SortIndex => SortSubs.IsChecked == true ? 1 : SortNew.IsChecked == true ? 2 : 0;
		async void More_Click(object sender, RoutedEventArgs e) => await Search(false);
		async void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await Search(true); }
		async void Sort_Checked(object sender, RoutedEventArgs e) { if (IsLoaded && wsOpened) await Search(true); }
		void WsRow_Click(object sender, MouseButtonEventArgs e) { var w = Ctx<WsItem>(sender); w.IsExpanded = !w.IsExpanded; }
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
				if (MessageBox.Show(this, $"Disinstallare \"{w.Title}\"?\n\nTi disiscrivo dal Workshop e la tolgo dalla cartella mods.", "BTO Sync", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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

		void SaveProfiles() { try { Core.SaveProfiles(profiles, activeProfile); } catch (Exception ex) { ShowError("Non riesco a salvare i profili.", ex); } RefreshProfiles(); }

		List<string> ActiveKeys() => local.Where(m => m.Enabled).Select(m => m.Key).ToList();

		void NewProfile_Click(object sender, RoutedEventArgs e)
		{
			var name = NewProfileName.Text.Trim();
			if (name == "") name = "Profilo " + (profiles.Count + 1);
			if (profiles.Any(p => p.name == name)) { MessageBox.Show(this, "Esiste già un profilo con questo nome.", "BTO Sync"); return; }
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
				var missing = Core.ApplyProfile(p, local);
				activeProfile = p.name;
				SaveProfiles();
				RefreshSummary();
				Status("Profilo applicato: " + p.name);
				if (missing.Count > 0)
					MessageBox.Show(this, $"{missing.Count} mod del profilo non sono installate. Per scaricarle esporta il codice del profilo da chi le ha e usa Gioca online.", "BTO Sync");
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
			if (MessageBox.Show(this, $"Eliminare il profilo \"{p.name}\"? Le mod restano installate.", "BTO Sync", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
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
		async void ImportProfile_Click(object sender, RoutedEventArgs e)
		{
			List<HostMod> host;
			try { host = Core.FromCode(ImportBox.Text); }
			catch (FormatException ex) { MessageBox.Show(this, ex.Message, "Codice non valido", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
			var name = "Importato " + DateTime.Now.ToString("dd/MM HH:mm");
			profiles.Add(new Profile { name = name, keys = host.Select(h => h.key).ToList() });
			SaveProfiles();
			CodeBox.Text = ImportBox.Text;
			ImportBox.Text = "";
			NavOnline.IsChecked = true;
			await Compare();
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
				SlotsList.ItemsSource = slots;
				BackupsList.ItemsSource = backups;
				BackupsEmpty.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
				int game = backups.Count(b => b.FromGame);
				BackupsCount.Text = backups.Count == 0 ? "" : $"{backups.Count - game} di BTO Sync · {game} copie automatiche del gioco";
				SavesDirText.Text = "Salvataggi del gioco: " + Saves.SaveDir;
			}
			catch (Exception ex) { ShowError("Non riesco a leggere i salvataggi.", ex); }
		}

		static readonly string NL = Environment.NewLine + Environment.NewLine;
		bool Ask(string text) => MessageBox.Show(this, text, "BTO Sync", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

		void SaveAction(string done, Action act)
		{
			UpdateState();
			if (isaac) { MessageBox.Show(this, "Chiudi Isaac prima: mentre è aperto il gioco riscrive i salvataggi.", "BTO Sync", MessageBoxButton.OK, MessageBoxImage.Information); return; }
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
				if (Ask($"Copiare lo {s.Title.ToLower()} nello {to.Title.ToLower()}?{NL}{Overwrites(to)}"))
					SaveAction($"{s.Title} copiato nello {to.Title.ToLower()}.", () => Saves.Copy(s.Slot, to.Slot));
			});
		}

		void BackupRestore_Click(object sender, RoutedEventArgs e)
		{
			var b = Ctx<SaveBackup>(sender);
			PickSlot(sender, 0, to =>
			{
				if (Ask($"Ripristinare il backup \"{b.Title}\" nello {to.Title.ToLower()}?{NL}{b.Sub}{NL}{Overwrites(to)}"))
					SaveAction($"Backup ripristinato nello {to.Title.ToLower()}.", () => Saves.Restore(b, to.Slot));
			});
		}

		void BackupDelete_Click(object sender, RoutedEventArgs e)
		{
			var b = Ctx<SaveBackup>(sender);
			if (Ask($"Eliminare il backup \"{b.Title}\"?{NL}{b.Sub}"))
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
