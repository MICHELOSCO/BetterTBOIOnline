using System.Linq;
using System.Windows;

namespace BtoSync
{
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			if (e.Args.Contains("--selftest")) { Shutdown(SelfTest.Run()); return; }
			Updater.CleanOld(Updater.AppDir);
			Core.Screenshots = e.Args.Contains("--screenshots");
			DispatcherUnhandledException += (s, x) =>  // an unexpected error must not close the window mid-sync
			{
				Dialog.Show(Current.MainWindow, "Errore imprevisto", x.Exception.Message, DialogKind.Error);
				x.Handled = true;
			};
			new MainWindow().Show();
		}
	}
}
