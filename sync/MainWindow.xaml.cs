using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
		Plan plan;
		int wsPage = 1;
		bool wsOpened, isaac, busy;

		public MainWindow()
		{
			InitializeComponent();
			ModsList.ItemsSource = mods;
			WsList.ItemsSource = ws;
			modsDir = Core.FindModsDir();
			var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
			timer.Tick += (s, e) => UpdateState();
			timer.Start();
			Loaded += async (s, e) => { UpdateState(); await Reload(); };
			Closing += (s, e) => Steam.Disconnect();
			SourceInitialized += (s, e) => { int on = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref on, 4); };  // dark title bar
		}

		[System.Runtime.InteropServices.DllImport("dwmapi.dll")]
		static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

		void Status(string s) => StatusText.Text = s;

		void UpdateState()
		{
			isaac = Core.IsaacRunning;
			if (isaac && Steam.Ready) Steam.Disconnect();  // Isaac needs Steam to itself
			IsaacBanner.Visibility = isaac ? Visibility.Visible : Visibility.Collapsed;
			SteamBox.Visibility = Steam.Ready ? Visibility.Visible : Visibility.Collapsed;
			ModsList.IsEnabled = !isaac && !busy;
			ApplyBtn.IsEnabled = !isaac && !busy;
			LaunchBtn.IsEnabled = !isaac;
		}

		bool Blocked()
		{
			UpdateState();
			if (isaac) { MessageBox.Show(this, "Chiudi Isaac prima: il gioco carica le mod solo all'avvio.", "BTO Sync", MessageBoxButton.OK, MessageBoxImage.Information); return true; }
			if (modsDir == null) { MessageBox.Show(this, "Non trovo la cartella mods di Isaac: indicala con Cambia.", "BTO Sync"); return true; }
			return false;
		}

		void ShowError(string what, Exception e) => MessageBox.Show(this, what + "\n\n" + e.Message, "BTO Sync", MessageBoxButton.OK, MessageBoxImage.Warning);

		async Task Reload()
		{
			if (modsDir == null) { ModsDirText.Text = "non trovata: premi Cambia e scegli la cartella mods di Isaac"; return; }
			ModsDirText.Text = modsDir;
			Status("Leggo le mod…");
			try { local = await Task.Run(() => Core.ScanMods(modsDir)); }
			catch (Exception e) { ShowError("Non riesco a leggere la cartella mods.", e); return; }
			mods.Clear();
			foreach (var m in local) mods.Add(m);
			ModsSummary.Text = $"{local.Count} mod installate · {local.Count(m => m.Enabled)} attive";
			try
			{
				var latest = await Core.LatestManifests(local.Where(m => m.Id != "").Select(m => m.Id).ToList());
				foreach (var m in local) m.UpdateStatus(latest);
				Status("Pronto.");
			}
			catch { foreach (var m in local) m.UpdateStatus(new Dictionary<string, string>()); Status("Workshop non raggiungibile: non posso controllare gli aggiornamenti."); }
			foreach (var w in ws) w.Installed = Core.HasWorkshopCopy(w.Id.ToString(), modsDir);
		}

		// ---- header ----
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

		void Disconnect_Click(object sender, RoutedEventArgs e) { Steam.Disconnect(); UpdateState(); }

		void Launch_Click(object sender, RoutedEventArgs e)
		{
			Steam.Disconnect();
			UpdateState();
			Process.Start("steam://rungameid/" + Core.AppId);
			Status("Avvio Isaac tramite Steam…");
		}

		void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (e.Source != Tabs || Tabs.SelectedIndex != 2 || wsOpened) return;
			wsOpened = true;
			_ = Search(true);
		}

		// ---- 1. installed mods ----
		async void Refresh_Click(object sender, RoutedEventArgs e) => await Reload();

		void OpenDir_Click(object sender, RoutedEventArgs e) { if (modsDir != null) Process.Start("explorer.exe", "\"" + modsDir + "\""); }

		void Toggle_Click(object sender, RoutedEventArgs e)
		{
			var m = (LocalMod)((FrameworkElement)sender).DataContext;
			try
			{
				if (Core.IsaacRunning) throw new InvalidOperationException("chiudi Isaac prima");
				Core.SetEnabled(m, m.Enabled);
				ModsSummary.Text = $"{local.Count} mod installate · {local.Count(x => x.Enabled)} attive";
				Status((m.Enabled ? "Attivata: " : "Disattivata: ") + m.Name);
			}
			catch (Exception ex) { m.Enabled = !m.Enabled; ShowError("Non posso cambiare " + m.Name + ".", ex); }
		}

		async void UpdateMod_Click(object sender, RoutedEventArgs e)
		{
			var m = (LocalMod)((FrameworkElement)sender).DataContext;
			if (Blocked()) return;
			var err = Steam.Connect();
			UpdateState();
			if (err != null) { MessageBox.Show(this, err, "BTO Sync"); return; }
			try
			{
				m.Status = "Scarico…"; m.CanUpdate = false;
				await Steam.SubscribeAndInstall(ulong.Parse(m.Id), modsDir, p => Dispatcher.BeginInvoke(new Action(() => m.Status = $"Scarico {p:P0}")));
				Status("Aggiornata: " + m.Name);
			}
			catch (Exception ex) { ShowError("Aggiornamento di " + m.Name + " non riuscito.", ex); }
			await Reload();
		}

		// ---- 2. online ----
		async void Export_Click(object sender, RoutedEventArgs e)
		{
			var on = local.Where(m => m.Enabled).ToList();
			var code = Core.ToCode(on);
			ExportBox.Text = code;  // visible copy even if the clipboard fails
			ExportBox.Visibility = Visibility.Visible;
			bool copied = NativeClipboard.SetText(code);
			ExportResult.Text = copied ? $"✓ Codice copiato ({on.Count} mod attive): incollalo su Discord agli altri giocatori."
				: "Non riesco a usare gli appunti: seleziona il codice qui sopra e copialo con Ctrl+C.";
			Status(copied ? "Codice dell'host copiato negli appunti." : "Appunti non disponibili.");

			// Mods the others will not be able to get automatically
			var warn = new List<string>();
			var onlyHere = on.Where(m => m.Id == "" && !Core.IsBto(m.Folder)).Select(m => m.Name).ToList();
			if (onlyHere.Count > 0) warn.Add("Non sono né sul Workshop né su GitHub (gli altri dovranno chiederti la cartella): " + string.Join(", ", onlyHere));
			var links = on.Where(m => m.Link).Select(m => m.Name).ToList();
			if (links.Count > 0) warn.Add("Collegate a una cartella di sviluppo (gli altri riceveranno la versione della release, forse diversa): " + string.Join(", ", links));
			try
			{
				var assets = await Core.BtoAssets();
				var unreleased = on.Where(m => Core.IsBto(m.Folder) && !m.Link && !assets.ContainsKey($"{m.Folder}-{m.Version}.zip")).Select(m => $"{m.Name} {m.Version}").ToList();
				if (unreleased.Count > 0) warn.Add("Non sono in nessuna release su GitHub, gli altri non potranno scaricarle (disattivale se non servono): " + string.Join(", ", unreleased));
			}
			catch { }
			ExportWarn.ItemsSource = warn;
		}

		void Paste_Click(object sender, RoutedEventArgs e)
		{
			try { if (Clipboard.ContainsText()) CodeBox.Text = Clipboard.GetText(); }
			catch (System.Runtime.InteropServices.COMException) { Status("Appunti occupati da un altro programma: riprova o incolla con Ctrl+V."); }
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
			if (plan.Actions.Count == 0 && plan.Notes.Count == 0) { PlanTitle.Text = "✓  Sei allineato all'host"; PlanSub.Text = $"Hai le stesse {host.Count} mod dell'host: puoi entrare in partita."; }
			else if (plan.Actions.Count == 0) { PlanTitle.Text = "Quasi pronto"; PlanSub.Text = "Non c'è niente da fare in automatico, ma controlla gli avvisi qui sotto."; }
			else { PlanTitle.Text = plan.Actions.Count == 1 ? "1 cosa da sistemare" : $"{plan.Actions.Count} cose da sistemare"; PlanSub.Text = "Premi Allinea ora: BTO Sync le fa tutte. Le mod disattivate restano installate."; }
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
					a.State = "✓ fatto";
				}
				catch (Exception ex) { a.State = "✗ " + ex.Message; warnings.Add(a.Text + ": " + ex.Message); }
			}
			busy = false; UpdateState();
			if (warnings.Count > 0) MessageBox.Show(this, string.Join("\n\n", warnings), "Alcune cose non sono andate", MessageBoxButton.OK, MessageBoxImage.Warning);
			await Compare();  // shows what is left
		}

		// ---- 3. workshop ----
		int SortIndex => SortSubs.IsChecked == true ? 1 : SortNew.IsChecked == true ? 2 : 0;

		async void Search_Click(object sender, RoutedEventArgs e) => await Search(true);
		async void More_Click(object sender, RoutedEventArgs e) => await Search(false);
		async void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await Search(true); }
		async void Sort_Checked(object sender, RoutedEventArgs e) { if (IsLoaded && wsOpened) await Search(true); }

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
				WsInfo.Text = total == 0 ? "Nessuna mod trovata." : $"{total:N0} mod · ne vedi {ws.Count}. Passa sopra una scheda per la descrizione.";
				MoreBtn.Visibility = ws.Count < total ? Visibility.Visible : Visibility.Collapsed;
			}
			catch (Exception ex) { WsInfo.Text = "Ricerca non riuscita: " + ex.Message; }
		}

		void WsPage_Click(object sender, RoutedEventArgs e)
		{
			var w = (WsItem)((FrameworkElement)sender).DataContext;
			Process.Start("steam://url/CommunityFilePage/" + w.Id);
		}

		async void WsAct_Click(object sender, RoutedEventArgs e)
		{
			var w = (WsItem)((FrameworkElement)sender).DataContext;
			if (Blocked()) return;
			var err = Steam.Connect();
			UpdateState();
			if (err != null) { w.Error = err; return; }
			w.Error = "";
			if (w.Installed)
			{
				if (MessageBox.Show(this, $"Rimuovere \"{w.Title}\"?\n\nTi disiscrivo dal Workshop e la tolgo dalla cartella mods.", "BTO Sync", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
				w.Busy = true;
				try { await Steam.Unsubscribe(w.Id); Core.RemoveWorkshopCopy(w.Id.ToString(), modsDir); w.Installed = false; Status("Rimossa: " + w.Title); }
				catch (Exception ex) { w.Error = ex.Message; }
			}
			else
			{
				w.Busy = true; w.Progress = 0;
				try
				{
					await Steam.SubscribeAndInstall(w.Id, modsDir, p => Dispatcher.BeginInvoke(new Action(() => w.Progress = p)));
					w.Installed = true;
					Status("Installata: " + w.Title + " (la trovi in Le mie mod, già attiva)");
				}
				catch (Exception ex) { w.Error = ex.Message; }
			}
			w.Busy = false;
			await Reload();
		}
	}
}

namespace BtoSync
{
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
