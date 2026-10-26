using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;

namespace BtoSync
{
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			if (e.Args.Contains("--selftest")) { Shutdown(SelfTest.Run()); return; }
			// the Steamworks child (Steam.cs): off the UI thread, so its awaits do not wait on a dispatcher nobody pumps
			if (e.Args.Contains("--steam-host")) { Shutdown(System.Threading.Tasks.Task.Run(() => Steam.Host()).GetAwaiter().GetResult()); return; }
			Updater.CleanOld(Updater.AppDir);
			Core.Screenshots = e.Args.Contains("--screenshots");
			DispatcherUnhandledException += (s, x) =>  // an unexpected error must not close the window mid-sync
			{
				Dialog.Show(Current.MainWindow, "Errore imprevisto", x.Exception.Message, DialogKind.Error);
				x.Handled = true;
			};
			if (LoneCopy()) return;
			new MainWindow().Show();
		}

		// BTO-Sync.exe copied somewhere by itself (instead of a shortcut): without the DLLs next to it the Workshop cannot work.
		// When BTO Sync is installed, offers to open that one and leave a shortcut to it in place of the copy. True: closing.
		bool LoneCopy()
		{
			if (Steam.FilesHere) return false;
			var here = Assembly.GetExecutingAssembly().Location;
			var installed = new[] { Registry.CurrentUser, Registry.LocalMachine }
				.Select(k => k.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6B1E3C9A-4F2D-4E8B-9C7A-B70517C0A2E1}_is1")?.GetValue("InstallLocation") as string)
				.Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BTO-Sync"))
				.Where(d => !string.IsNullOrEmpty(d)).Select(d => Path.Combine(d, "BTO-Sync.exe"))
				.FirstOrDefault(f => File.Exists(f) && File.Exists(Path.Combine(Path.GetDirectoryName(f), "Facepunch.Steamworks.Win64.dll"))
					&& !string.Equals(Path.GetFullPath(f), Path.GetFullPath(here), StringComparison.OrdinalIgnoreCase));
			ShutdownMode = ShutdownMode.OnExplicitShutdown;  // the dialog is the only window yet: closing it must not end the app
			try
			{
				if (installed == null)
				{
					Dialog.Show(null, "Mancano dei file di BTO Sync", "Questo BTO-Sync.exe è da solo: senza gli altri file della sua cartella il Workshop non funziona. "
						+ "Reinstalla BTO Sync (o estrai tutto lo zip) e aprilo da lì. Per averlo sul desktop: tasto destro sull'exe › Mostra altre opzioni › Invia a › Desktop (crea collegamento).",
						DialogKind.Warning, "Apri lo stesso");
					return false;
				}
				if (!Dialog.Show(null, "Questa è una copia di BTO Sync", "Questo BTO-Sync.exe è stato copiato da solo, senza i file che gli servono per il Workshop. "
					+ $"BTO Sync è installato in {Path.GetDirectoryName(installed)}: apro quello e lascio qui un collegamento al posto della copia?",
					DialogKind.Question, "Sì, apri quello", "Usa questa copia")) return false;
				try
				{
					var sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
					var lnk = sh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { Path.Combine(Path.GetDirectoryName(here), "BTO Sync.lnk") });
					lnk.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { installed });
					lnk.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(installed) });
					lnk.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
					// the copy is still running: deleted a moment after it closes
					Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & del \"{here}\"") { CreateNoWindow = true, UseShellExecute = false });
				}
				catch (Exception ex) { Core.LogError(ex); }  // no shortcut: the installed one opens anyway
				Process.Start(installed);
				Shutdown();
				return true;
			}
			finally { ShutdownMode = ShutdownMode.OnLastWindowClose; }
		}
	}
}
