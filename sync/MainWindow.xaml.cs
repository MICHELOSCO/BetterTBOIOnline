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
			TagsList.ItemsSource = tagChips;
			ProfilesList.ItemsSource = profiles;
			var modsView = CollectionViewSource.GetDefaultView(mods);
			modsView.Filter = ModFilter;
			modsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LocalMod.Group)));
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
			Closing += (s, e) => { Steam.Disconnect(); SaveWindow(); };
			RestoreWindow();
			KeyDown += (s, e) =>  // Esc closes the Workshop detail pane
			{
				if (e.Key == Key.Escape && UpdModal.Visibility == Visibility.Visible) { UpdModal.Visibility = Visibility.Collapsed; e.Handled = true; }
				else if (e.Key == Key.Escape && ModModal.Visibility == Visibility.Visible) { ShowMod(null); e.Handled = true; }
				else if (e.Key == Key.Escape && WsModal.Visibility == Visibility.Visible) { ShowDetail(null); e.Handled = true; }
			};
			KeyDown += (s, e) =>  // Ctrl+F: search the page that has a search box
			{
				if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;
				if (NavWs.IsChecked != true) NavMods.IsChecked = true;
				(NavWs.IsChecked == true ? SearchBox : ModsSearch).Focus();
				e.Handled = true;
			};
			SourceInitialized += (s, e) => { int on = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref on, 4); };  // dark title bar
		}

		// Window size: the last one used, else 1440x900; never larger than the screen's work area.
		static string WindowFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BtoSync", "window.txt");

		void RestoreWindow()
		{
			double w = Width, h = Height; bool max = false;
			try
			{
				var p = System.IO.File.ReadAllText(WindowFile).Split(' ');
				w = double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture); h = double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture); max = p.Length > 2 && p[2] == "max";
			}
			catch { }  // first start: the XAML default
			var area = SystemParameters.WorkArea;
			Width = Math.Max(MinWidth, Math.Min(w, area.Width * 0.95));
			Height = Math.Max(MinHeight, Math.Min(h, area.Height * 0.95));
			if (max) WindowState = WindowState.Maximized;
		}

		void SaveWindow()
		{
			try
			{
				var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
				System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WindowFile));
				System.IO.File.WriteAllText(WindowFile, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} {1:0}{2}", r.Width, r.Height, WindowState == WindowState.Maximized ? " max" : ""));
			}
			catch { }
		}

		[System.Runtime.InteropServices.DllImport("dwmapi.dll")]
		static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

		static T Ctx<T>(object sender) => (T)((FrameworkElement)sender).DataContext;
		void Status(string s) => StatusText.Text = s;

		// ---------- loading bar: any running operation (counted) or a busy action shows it ----------
		int loading;
		void Loading(bool on) { loading += on ? 1 : -1; ShowLoad(); }
		void ShowLoad() => LoadBar.Visibility = loading > 0 || busy ? Visibility.Visible : Visibility.Collapsed;
		void LoadBar_SizeChanged(object sender, SizeChangedEventArgs e) =>
			LoadRunX.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(-LoadRun.Width, e.NewSize.Width, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });

		void UpdateState()
		{
			ShowLoad();
			bool was = isaac;
			isaac = Core.IsaacRunning;
			if (isaac && Steam.Ready) Steam.Disconnect();  // Isaac needs Steam to itself
			// the Workshop needs Steam: with Isaac open it says so instead of looking empty, and starts again once it closes
			WsIsaac.Visibility = isaac ? Visibility.Visible : Visibility.Collapsed;
			WsList.Visibility = isaac ? Visibility.Collapsed : Visibility.Visible;
			SearchBox.IsEnabled = SortBtn.IsEnabled = TagsBtn.IsEnabled = !isaac;
			if (isaac) { WsEmpty.Visibility = MoreBtn.Visibility = WsModal.Visibility = Visibility.Collapsed; WsInfo.Text = "Il Workshop si usa a gioco chiuso."; }
			else if (was && wsOpened) _ = Search(true);
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
			Core.LogError(e);
			var hint = e.GetType() == typeof(Exception) ? null : Core.ErrorHint(e);
			return hint == null ? e.Message : hint + NL + "Dettaglio: " + e.Message;
		}

		// the short form for messages inside the window: the system detail goes only to errori.log (and a tooltip where there is one)
		static string Short(Exception e) { Core.LogError(e); return Core.ErrorShort(e); }

		// tie: also bring the BTO mods in line with the Workshop mods (not when comparing: there the host's list wins)
		async Task Reload(bool tie = true) { Loading(true); try { await ReloadCore(tie); } finally { Loading(false); } }
		async Task ReloadCore(bool tie = true)
		{
			if (modsDir == null) { ModsDirText.Text = "Non trovata: premi Cambia e scegli la cartella mods di Isaac."; ModsSummary.Text = "Cartella mods non trovata: impostala in Impostazioni."; return; }
			ModsDirText.Text = modsDir;
			Status("Leggo le mod…");
			try { local = await Task.Run(() => Core.ScanMods(modsDir)); }
			catch (Exception e) { ShowError("Non riesco a leggere la cartella mods.", e); return; }
			var shown = (ModDetail.Content as LocalMod)?.Key;
			mods.Clear();
			foreach (var m in local.OrderBy(m => m.Managed)) { m.Unlocked = !isaac && !busy; mods.Add(m); }
			if (ModModal.Visibility == Visibility.Visible) ShowMod(local.FirstOrDefault(m => m.Key == shown));  // updated: same mod, removed: closed
			try
			{
				var latest = await Core.LatestManifests(local.Where(m => m.Id != "").Select(m => m.Id).ToList());
				foreach (var m in local) m.UpdateStatus(latest);
				wsChecked = true;
				Status("Pronto.");
			}
			catch { wsChecked = false; foreach (var m in local) m.UpdateStatus(new Dictionary<string, string>()); Status("Workshop non raggiungibile: non posso controllare gli aggiornamenti."); }
			foreach (var w in ws) w.Installed = Core.HasWorkshopCopy(w.Id.ToString(), modsDir);
			RefreshSummary();
			_ = CheckDeps();
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
				catch (Exception ex) { a.State = "errore"; warnings.Add(a.Text + ": " + Short(ex)); }
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
			Status("");  // a message belongs to the page that wrote it
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

		void ModsSearch_TextChanged(object sender, TextChangedEventArgs e)
		{
			CollectionViewSource.GetDefaultView(mods).Refresh();
			if (ModsSearch.Text.Trim() != "" && mods.Any(m => m.Managed && ModFilter(m))) BtoOpen = true;  // don't hide matches in the closed group
		}

		// the "Fix BTO" group in Le mie mod: closed by default, stays as the user left it across reloads
		public static readonly DependencyProperty BtoOpenProperty = DependencyProperty.Register(nameof(BtoOpen), typeof(bool), typeof(MainWindow));
		public bool BtoOpen { get => (bool)GetValue(BtoOpenProperty); set => SetValue(BtoOpenProperty, value); }
		void Filter_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) CollectionViewSource.GetDefaultView(mods).Refresh(); }
		void OpenDir_Click(object sender, RoutedEventArgs e) { if (modsDir != null) Process.Start("explorer.exe", "\"" + modsDir + "\""); }
		void ModRow_Click(object sender, MouseButtonEventArgs e) => ShowMod(Ctx<LocalMod>(sender));
		void ShowMod(LocalMod m) { ModDetail.Content = m; ModModal.Visibility = m == null ? Visibility.Collapsed : Visibility.Visible; }
		void CloseMod_Click(object sender, RoutedEventArgs e) => ShowMod(null);
		void ModBackdrop_Click(object sender, MouseButtonEventArgs e) => ShowMod(null);
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
			if (m.Enabled && m.Id != "") EnableRequired(m);
			_ = CheckDeps();
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

		// ---------- "Cerca aggiornamenti": check everything, then choose what to update ----------
		bool wsChecked;
		List<UpdEntry> updates = new List<UpdEntry>();

		async void CheckUpdates_Click(object sender, RoutedEventArgs e)
		{
			CheckUpdBtn.IsEnabled = false; CheckUpdText.Text = "Controllo…";
			try { await Reload(); await CheckUpdate(); }
			finally { CheckUpdBtn.IsEnabled = true; CheckUpdText.Text = "Cerca aggiornamenti"; }
			updates = local.Where(m => m.CanUpdate).Select(m => new UpdEntry { Name = m.Name, Detail = "Mod del Workshop: c'è una versione nuova" + (m.Version != "" ? $" (hai la {m.Version})" : ""), Mod = m }).ToList();
			if (update != null)
				updates.Add(new UpdEntry { Name = "BTO Sync " + update.VersionText, Detail = $"Il programma (hai la {Updater.Current.ToString(3)}): si aggiorna per ultimo e si riavvia", IsApp = true });
			int ws = local.Count(m => m.Id != "");
			if (updates.Count == 0)
			{
				if (!wsChecked) Dialog.Show(this, "Non riesco a controllare le mod", "Il Workshop non risponde: controlla la connessione e riprova.", DialogKind.Warning);
				else Dialog.Show(this, "È tutto aggiornato", $"Ho controllato {ws} mod del Workshop e BTO Sync. Le mod BTO si aggiornano da sole.");
				return;
			}
			UpdList.ItemsSource = updates;
			UpdTitle.Text = updates.Count == 1 ? "1 aggiornamento disponibile" : $"{updates.Count} aggiornamenti disponibili";
			RefreshUpdButtons();
			UpdModal.Visibility = Visibility.Visible;
		}

		void RefreshUpdButtons()
		{
			int n = updates.Count(u => u.On);
			UpdSelText.Text = $"Aggiorna selezionate ({n})";
			UpdSelBtn.IsEnabled = n > 0;
		}

		void UpdToggle_Click(object sender, RoutedEventArgs e) => RefreshUpdButtons();
		void UpdCancel_Click(object sender, RoutedEventArgs e) => UpdModal.Visibility = Visibility.Collapsed;
		void UpdBackdrop_Click(object sender, MouseButtonEventArgs e) => UpdModal.Visibility = Visibility.Collapsed;
		async void UpdSelected_Click(object sender, RoutedEventArgs e) => await RunUpdates(updates.Where(u => u.On).ToList());
		async void UpdAll_Click(object sender, RoutedEventArgs e) => await RunUpdates(updates.ToList());

		// Workshop mods one after the other (each row shows its progress), then BTO Sync itself, which restarts.
		async Task RunUpdates(List<UpdEntry> todo)
		{
			UpdModal.Visibility = Visibility.Collapsed;
			var mods = todo.Where(u => u.Mod != null).Select(u => u.Mod).ToList();
			if (mods.Count > 0)
			{
				if (Blocked()) return;
				NavMods.IsChecked = true;
				int ok = 0;
				Loading(true);
				try { foreach (var m in mods) if (await UpdateWorkshopMod(m)) ok++; }
				finally { Loading(false); }
				await Reload();
				Status(ok == mods.Count ? (ok == 1 ? "Aggiornata 1 mod." : $"Aggiornate {ok} mod.") : $"Aggiornate {ok} mod su {mods.Count}.");
			}
			if (todo.Any(u => u.IsApp) && update != null) await InstallAppUpdate();
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
			var code = Core.ToCode(on, await ReleaseValues(on));
			int shared = on.Count(m => !m.Private);
			ExportBox.Text = code;  // visible copy even if the clipboard fails
			ExportBox.Visibility = Visibility.Visible;
			bool copied = NativeClipboard.SetText(code);
			ExportResult.Text = copied ? $"Codice copiato ({shared} mod attive): incollalo su Discord agli altri giocatori."
				: "Non riesco a usare gli appunti: seleziona il codice qui sopra e copialo con Ctrl+C.";
			Status(copied ? "Codice dell'host copiato negli appunti." : "Appunti non disponibili.");
			var warn = new List<string>();
			var onlyHere = on.Where(m => m.Id == "" && !Core.IsBto(m.Folder) && !m.Private).Select(m => m.Name).ToList();
			if (onlyHere.Count > 0) warn.Add("Né sul Workshop né su GitHub, gli altri dovranno chiederti la cartella: " + string.Join(", ", onlyHere));
			var links = on.Where(m => m.Link).Select(m => m.Name).ToList();
			if (links.Count > 0) warn.Add("Collegate a una cartella di sviluppo: nel codice c'è la loro release su GitHub, che è quella che scaricano gli altri. Se la tua copia è diversa, la partita può dividersi: " + string.Join(", ", links));
			try
			{
				var assets = await Core.BtoAssets();
				var unreleased = on.Where(m => m.Bto && !m.Link && !assets.ContainsKey($"{m.Folder}-{m.Version}.zip")).Select(m => $"{m.Name} {m.Version}").ToList();
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
				if (!clip.Contains("BTO1:") && !clip.Contains("BTO2:")) return;
				Core.FromCode(clip);
				NavOnline.IsChecked = true;
				CodeBox.Text = clip;
			}
			catch (Exception) { }  // clipboard busy or not a code: start as usual
		}

		async Task Compare() { Loading(true); try { await CompareCore(); } finally { Loading(false); } }
		async Task CompareCore()
		{
			List<HostMod> host;
			try { host = Core.FromCode(CodeBox.Text); }
			catch (FormatException) { return; }  // CodeBox_TextChanged already shows why
			await Reload(false);
			Status("Confronto con l'host…");
			var ids = host.Where(h => !string.IsNullOrEmpty(h.id)).Select(h => h.id).Union(local.Where(l => l.Id != "").Select(l => l.Id)).ToList();
			Dictionary<string, string> latest;
			try { latest = await Core.LatestManifests(ids); } catch { latest = new Dictionary<string, string>(); }
			Core.FillNames(host, local);
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
		readonly List<TagChip> tagChips = TagChip.All.Select(t => new TagChip { Name = t.name, Label = t.label }).ToList();
		int sortIndex, trendDays = 7;  // see Steam.Search; 5 = relevance, only with a text
		bool sortByHand;               // the user picked an order: typing no longer switches it to relevance
		DispatcherTimer typing;

		static readonly (int sort, int days, string text)[] Sorts =
		{
			(0, 1, "Più popolari · oggi"), (0, 7, "Più popolari · settimana"), (0, 90, "Più popolari · 3 mesi"), (0, 365, "Più popolari · anno"), (0, -1, "Più popolari · sempre"),
			(1, 7, "Più votate"), (2, 7, "Più iscritti"), (3, 7, "Più recenti"), (4, 7, "Aggiornate di recente"), (5, 7, "Più pertinenti alla ricerca"),
		};

		string SortName => Sorts.First(x => x.sort == sortIndex && (x.sort != 0 || x.days == trendDays)).text;

		void Sort_Click(object sender, RoutedEventArgs e)
		{
			var menu = new ContextMenu { PlacementTarget = SortBtn, Placement = PlacementMode.Bottom };
			bool hasText = !string.IsNullOrWhiteSpace(SearchBox.Text);
			foreach (var x in Sorts)
			{
				if (x.sort == 5 && !hasText) continue;
				if (x.sort == 1 || x.sort == 5) menu.Items.Add(new Separator { Background = Res("Line"), Margin = new Thickness(6, 4, 6, 4) });
				bool on = x.sort == sortIndex && (x.sort != 0 || x.days == trendDays);
				var head = new StackPanel { Orientation = Orientation.Horizontal };
				head.Children.Add(new TextBlock { Text = on ? "" : "", Width = 22, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, Foreground = Res("Accent"), VerticalAlignment = VerticalAlignment.Center });
				head.Children.Add(new TextBlock { Text = x.text });
				var item = new MenuItem { Header = head };
				item.Click += async (o, a) => { sortIndex = x.sort; trendDays = x.days; sortByHand = true; SortText.Text = SortName; await Search(true); };
				menu.Items.Add(item);
			}
			menu.IsOpen = true;
		}

		async void More_Click(object sender, RoutedEventArgs e) => await Search(false);
		async void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { typing?.Stop(); await Search(true); } }

		// Searches by itself after a short pause in typing; an empty box goes back to the chosen order.
		void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (!IsLoaded || !wsOpened) return;
			bool hasText = !string.IsNullOrWhiteSpace(SearchBox.Text);
			if (hasText && !sortByHand) sortIndex = 5;
			if (!hasText && sortIndex == 5) { sortIndex = 0; sortByHand = false; }
			SortText.Text = SortName;
			if (typing == null)
			{
				typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
				typing.Tick += async (s, a) => { typing.Stop(); await Search(true); };
			}
			typing.Stop(); typing.Start();
		}

		void Filters_Click(object sender, RoutedEventArgs e)
		{
			TagsPopup.HorizontalOffset = TagsBtn.ActualWidth - 560;  // right-aligned under the button
			TagsPopup.IsOpen = !TagsPopup.IsOpen;
		}

		async void Tag_Click(object sender, RoutedEventArgs e) => await Search(true);
		async void ClearTags_Click(object sender, RoutedEventArgs e) { foreach (var t in tagChips) t.On = false; await Search(true); }
		// columns of about 200 px; the details open over the grid, so they never change
		void WsList_SizeChanged(object sender, SizeChangedEventArgs e) => WsList.Tag = Math.Max(2, (int)((e.NewSize.Width - 4) / 200));
		void WsPage_Click(object sender, RoutedEventArgs e) => Process.Start("steam://url/CommunityFilePage/" + Ctx<WsItem>(sender).Id);
		void WsTile_KeyDown(object sender, KeyEventArgs e) { if ((e.Key == Key.Enter || e.Key == Key.Space) && !Ctx<WsItem>(sender).Skeleton) { ShowDetail(Ctx<WsItem>(sender)); e.Handled = true; } }
		void WsTile_Click(object sender, MouseButtonEventArgs e) { if (!Ctx<WsItem>(sender).Skeleton) ShowDetail(Ctx<WsItem>(sender)); }
		void CloseDetail_Click(object sender, RoutedEventArgs e) => ShowDetail(null);

		// The detail pane on the right: big preview, numbers, tags, required mods, description, install.
		void ModalBackdrop_Click(object sender, MouseButtonEventArgs e) => ShowDetail(null);

		async void ShowDetail(WsItem w)
		{
			foreach (var x in ws) x.IsSelected = x == w;
			WsDetail.Content = w;
			WsModal.Visibility = w == null ? Visibility.Collapsed : Visibility.Visible;
			if (w == null) return;
			try
			{
				var r = await Core.RequiredItems(w.Id.ToString());
				w.RequiresText = r.Count == 0 ? "" : "Richiede " + string.Join(", ", r.Select(x => x.name)) + ": BTO Sync la installa insieme a questa mod.";
			}
			catch { }
		}

		int searchId;

		async Task Search(bool reset) { Loading(true); try { await SearchCore(reset); } finally { Loading(false); } }
		async Task SearchCore(bool reset)
		{
			UpdateState();
			if (isaac) return;  // UpdateState shows why and searches again when Isaac closes
			var err = Steam.Connect();
			UpdateState();
			if (err != null) { WsInfo.Text = err; return; }
			var on = tagChips.Where(t => t.On).ToList();
			ActiveTags.ItemsSource = on;
			ActiveTags.Visibility = on.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
			FiltersText.Text = on.Count == 0 ? "Tag" : $"Tag ({on.Count})";
			SortText.Text = SortName;
			if (reset)
			{
				ShowDetail(null);
				ws.Clear(); wsPage = 1;
				for (int k = 0; k < 8; k++) ws.Add(new WsItem { Skeleton = true });  // shape of what is coming, not an empty page
			}
			WsEmpty.Visibility = Visibility.Collapsed;
			WsInfo.Text = "Cerco…";
			MoreBtn.Visibility = Visibility.Collapsed;
			int search = ++searchId;
			try
			{
				var (items, total) = await Steam.Search(SearchBox.Text, sortIndex, trendDays, on.Select(t => t.Name), wsPage);
				if (search != searchId) return;  // a newer search started meanwhile
				foreach (var sk in ws.Where(x => x.Skeleton).ToList()) ws.Remove(sk);
				foreach (var i in items) ws.Add(WsItem.From(i, modsDir));
				wsPage++;
				WsInfo.ToolTip = null;
				WsInfo.Text = total == 0 ? "Nessuna mod trovata." : $"{total:N0} mod trovate · ne vedi {ws.Count} · clicca una mod per i dettagli";
				MoreBtn.Visibility = ws.Count < total ? Visibility.Visible : Visibility.Collapsed;
				WsEmpty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
				WsEmptyClear.Visibility = on.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
				WsEmptySub.Text = on.Count > 0 ? "Prova con meno tag o con altre parole." : "Prova con altre parole.";
			}
			catch (Exception ex)
			{
				if (search != searchId) return;
				foreach (var sk in ws.Where(x => x.Skeleton).ToList()) ws.Remove(sk);
				WsInfo.Text = "Ricerca non riuscita. " + Short(ex);
				WsInfo.ToolTip = ex.Message;
			}
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
				catch (Exception ex) { w.Error = Short(ex); }
			}
			else
			{
				w.Busy = true; w.Progress = 0;
				try
				{
					await Steam.SubscribeAndInstall(w.Id, modsDir, p => Dispatcher.BeginInvoke(new Action(() => w.Progress = p)));
					w.Installed = true;
					// Steam asks for the required items only on its web page: here they are installed with the mod
					var with = new List<string>();
					List<(string id, string name)> reqs;
					try { reqs = await Core.RequiredItems(w.Id.ToString()); } catch { reqs = new List<(string id, string name)>(); }
					foreach (var r in reqs.Where(r => !Core.HasWorkshopCopy(r.id, modsDir)))
					{
						w.Progress = 0;
						await Steam.SubscribeAndInstall(ulong.Parse(r.id), modsDir, p => Dispatcher.BeginInvoke(new Action(() => w.Progress = p)));
						with.Add(r.name);
					}
					Status("Installata e attiva: " + w.Title + (with.Count > 0 ? $", con {string.Join(", ", with)} (le serve)." : "."));
				}
				catch (Exception ex) { w.Error = Short(ex); }
			}
			w.Busy = false;
			await Reload();
		}

		// ---------- profiles ----------
		void RefreshProfiles()
		{
			var on = local.Where(m => m.Enabled).Select(m => m.Key).ToList();
			foreach (var p in profiles) { p.IsActive = p.name == activeProfile; p.IsCurrent = p.IsActive && new HashSet<string>(p.keys).SetEquals(on); }
			ProfilesEmpty.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			var cur = profiles.FirstOrDefault(p => p.IsActive);
			ActiveProfileText.Text = cur?.name ?? "Nessun profilo";
			ActiveProfileSub.Text = cur == null ? "Crea un profilo dalle mod attive" : cur.IsCurrent ? cur.Summary : cur.Summary + " · modificato";
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
			var now = ActiveKeys();
			if (!now.OrderBy(k => k).SequenceEqual(p.keys.OrderBy(k => k))
				&& !Dialog.Show(this, $"Sostituire il profilo \"{p.name}\" con le mod attive adesso?",
					$"Oggi ha {p.keys.Count} mod, dopo ne avrà {now.Count}. L'elenco di prima non si potrà recuperare.", DialogKind.Danger, "Sostituisci", "Annulla")) return;
			p.keys = now;
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

		// "⋯" on a profile row: the actions used less often than Applica, same look as the Workshop's Ordina menu
		void ProfileMenu_Click(object sender, RoutedEventArgs e)
		{
			var p = Ctx<Profile>(sender);
			var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
			void Add(string icon, string text, RoutedEventHandler click, Brush fg = null)
			{
				var head = new StackPanel { Orientation = Orientation.Horizontal };
				head.Children.Add(new TextBlock { Text = icon, Width = 24, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, Foreground = fg ?? Res("Muted"), VerticalAlignment = VerticalAlignment.Center });
				head.Children.Add(new TextBlock { Text = text, Foreground = fg ?? Res("Fg") });
				var item = new MenuItem { Header = head, DataContext = p };
				item.Click += click;
				menu.Items.Add(item);
			}
			Add("", "Copia il codice da mandare agli altri", ExportProfile_Click);
			Add("", "Sostituisci con le mod attive adesso", OverwriteProfile_Click);
			menu.Items.Add(new Separator { Background = Res("Line"), Margin = new Thickness(6, 4, 6, 4) });
			Add("", "Elimina il profilo", DeleteProfile_Click, new SolidColorBrush(Color.FromRgb(0xF0, 0x61, 0x6D)));
			// right edge under the button's right edge: the button sits at the window's right side
			menu.Opened += (o, a) => menu.HorizontalOffset = ((FrameworkElement)sender).ActualWidth - menu.ActualWidth;
			menu.IsOpen = true;
		}

		// BTO mods linked to a dev folder go into codes as their latest GitHub release (version + hash of the zip).
		async Task<Dictionary<string, (string version, string hash)>> ReleaseValues(IEnumerable<LocalMod> mods)
		{
			var r = new Dictionary<string, (string version, string hash)>();
			var linked = mods.Where(m => m.Link && Core.IsBto(m.Folder)).ToList();
			if (linked.Count == 0) return r;
			Loading(true);
			try
			{
				var catalog = Core.Catalog(await Assets());
				foreach (var m in linked)
					if (catalog.TryGetValue(m.Folder, out var c) && c.url != null) r[m.Folder] = (c.version, await Core.ReleaseHash(c.url, m.Folder));
			}
			catch { }  // offline: the code keeps the local copy
			finally { Loading(false); }
			return r;
		}

		async void ExportProfile_Click(object sender, RoutedEventArgs e)
		{
			var p = Ctx<Profile>(sender);
			var keys = new HashSet<string>(p.keys);
			var chosen = local.Where(m => keys.Contains(m.Key)).ToList();
			var code = Core.ToCode(chosen, await ReleaseValues(chosen));
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

		// ---------- Workshop dependencies ("Required items" on Steam) ----------
		List<Core.DepIssue> depIssues = new List<Core.DepIssue>();

		// Reads the required items of the active Workshop mods (cached), shows them in the details and warns about gaps.
		async Task CheckDeps() { Loading(true); try { await CheckDepsCore(); } finally { Loading(false); } }
		async Task CheckDepsCore()
		{
			var ids = local.Where(m => m.Id != "" && m.Enabled).Select(m => m.Id).Distinct().ToList();
			await Task.WhenAll(ids.Select(async id => { try { await Core.RequiredItems(id); } catch { } }));  // offline: only what is known
			foreach (var m in local.Where(m => m.Id != ""))
			{
				var r = Core.KnownRequired(m.Id);
				m.RequiresText = r == null || r.Count == 0 ? "" : "Richiede: " + string.Join(", ", r.Select(x => x.name));
			}
			depIssues = Core.DepIssues(local);
			DepBanner.Visibility = depIssues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
			DepBannerText.Text = string.Join(" ", depIssues.Select(d => $"{d.Mod.Name} richiede {d.Name}, che {(d.Req == null ? "non è installata" : "è spenta")}."));
		}

		// Turning a mod on also turns on the mods it needs that are installed but off (no download here).
		void EnableRequired(LocalMod m)
		{
			var on = new List<string>();
			foreach (var r in Core.KnownRequired(m.Id) ?? new List<(string id, string name)>())
			{
				var l = local.FirstOrDefault(x => x.Id == r.id);
				if (l == null || l.Enabled || l.Link) continue;
				try { Core.SetEnabled(l, true); on.Add(l.Name); } catch { }
			}
			if (on.Count > 0) { RefreshSummary(); Status($"Attivata: {m.Name}, e con lei {string.Join(", ", on)} (le serve)."); }
		}

		async void FixDeps_Click(object sender, RoutedEventArgs e)
		{
			if (depIssues.Count == 0 || Blocked()) return;
			busy = true; UpdateState();
			var done = new List<string>(); var failed = new List<string>();
			foreach (var d in depIssues.GroupBy(d => d.Id).Select(g => g.First()))
			{
				try
				{
					if (d.Req != null) Core.SetEnabled(d.Req, true);
					else
					{
						var err = Steam.Connect();
						UpdateState();
						if (err != null) throw new Exception(err);
						Status($"Scarico {d.Name} dal Workshop…");
						await Steam.SubscribeAndInstall(ulong.Parse(d.Id), modsDir, p => Dispatcher.BeginInvoke(new Action(() => Status($"Scarico {d.Name} {p:P0}"))));
					}
					done.Add(d.Name);
				}
				catch (Exception ex) { failed.Add($"{d.Name}: {Short(ex)}"); }
			}
			busy = false; UpdateState();
			if (failed.Count > 0) Dialog.Show(this, "Alcune mod richieste non sono state sistemate", string.Join(NL, failed), DialogKind.Warning);
			await Reload();
			if (done.Count > 0) Status("Mod richieste sistemate: " + string.Join(", ", done) + ".");
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

		async Task CheckUpdate() { Loading(true); try { await CheckUpdateCore(); } finally { Loading(false); } }
		async Task CheckUpdateCore()
		{
			UpdateText.Text = "Controllo gli aggiornamenti su GitHub…";
			try
			{
				var r = await Updater.Latest();
				update = r != null && r.Version > Updater.Current ? r : null;
				UpdateText.ToolTip = null;
				UpdateText.Text = update != null ? $"È disponibile la versione {update.VersionText}: premi Aggiorna."
					: r == null ? "Nessuna versione pubblicata su GitHub." : "Hai l'ultima versione.";
			}
			catch (Exception ex) { update = null; UpdateText.Text = "Non riesco a controllare gli aggiornamenti. " + Short(ex); UpdateText.ToolTip = ex.Message; }
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
			await InstallAppUpdate();
		}

		async Task InstallAppUpdate()
		{
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

	// One line of the update menu: a Workshop mod or BTO Sync itself.
	public class UpdEntry : Notify
	{
		bool on = true;
		public string Name { get; set; }
		public string Detail { get; set; }
		public bool On { get => on; set => Set(ref on, value); }
		public LocalMod Mod;
		public bool IsApp;
	}
}
