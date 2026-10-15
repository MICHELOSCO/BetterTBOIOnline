using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BtoSync
{
	// Offline self-check on fake mods folders: BTO-Sync.exe --selftest | more   (exit code 0 = ok)
	static class SelfTest
	{
		static void Assert(bool ok, string what) { if (!ok) throw new Exception("FALLITO: " + what); Console.WriteLine("pass " + what); }

		static void Mod(string dir, string folder, string name, string id, string body)
		{
			Directory.CreateDirectory(Path.Combine(dir, folder));
			File.WriteAllText(Path.Combine(dir, folder, "metadata.xml"), $"<metadata><name>{name}</name><directory>{folder.Split('_')[0]}</directory><id>{id}</id><version>1.0</version></metadata>");
			File.WriteAllText(Path.Combine(dir, folder, "main.lua"), body);
		}

		// A save file with the same chunk layout as the game's: achievements, counters (1 Mom's Heart, 10 deaths), level counters, items seen.
		static byte[] FakeSave(int achievements, int deaths, int items)
		{
			var ms = new MemoryStream();
			var w = new BinaryWriter(ms);
			w.Write(System.Text.Encoding.ASCII.GetBytes("ISAACNGSAVE09R  ")); w.Write(0);
			w.Write(1); w.Write(642); w.Write(642); for (int i = 0; i < 642; i++) w.Write((byte)(i >= 1 && i <= achievements ? 1 : 0));
			w.Write(2); w.Write(523 * 4); w.Write(523); for (int i = 0; i < 523; i++) w.Write(i == 10 ? deaths : i == 1 && deaths > 0 ? 3 : 0);
			w.Write(3); w.Write(14 * 4); w.Write(14); for (int i = 0; i < 14; i++) w.Write(0);
			w.Write(4); w.Write(733 * 4); w.Write(733); for (int i = 0; i < 733; i++) w.Write((byte)(i < items ? 1 : 0));
			w.Write(0x12345678);
			return ms.ToArray();
		}

		static void TestSaves(string t)
		{
			string save = Path.Combine(t, "remote"), data = Path.Combine(t, "game", "data");
			Directory.CreateDirectory(save); Directory.CreateDirectory(Path.Combine(data, "eid")); Directory.CreateDirectory(Path.Combine(data, "stageapi"));
			Saves.SaveDir = save; Saves.DataDir = data; Saves.BackupDir = Path.Combine(t, "backups"); Saves.DocsDir = Path.Combine(t, "docs");
			File.WriteAllBytes(Path.Combine(save, "rep+persistentgamedata1.dat"), FakeSave(109, 54, 268));
			File.WriteAllBytes(Path.Combine(save, "rep+gamestate1.dat"), new byte[] { 1 });
			File.WriteAllText(Path.Combine(data, "eid", "save1.dat"), "eid1");
			File.WriteAllBytes(Path.Combine(save, "rep+persistentgamedata2.dat"), FakeSave(5, 0, 0));
			File.WriteAllText(Path.Combine(data, "stageapi", "save2.dat"), "old2");

			var slots = Saves.Slots();
			var i1 = slots[0].Info;
			Assert(i1.Valid && i1.Achievements == 109 && i1.AchievementTotal == 641 && i1.Deaths == 54 && i1.MomKills == 3 && i1.Items == 268, "salvataggio letto: obiettivi, morti, Cuore di Mamma, oggetti");
			Assert(slots[0].HasRun && !slots[1].HasRun && !slots[1].Info.Played && !slots[2].Exists, "slot: partita in corso, mai giocato, vuoto");
			Assert(!SaveInfo.Read(Path.Combine(data, "eid", "save1.dat")).Valid, "un file che non è un salvataggio non viene letto");

			Saves.Copy(1, 2);
			Assert(File.ReadAllBytes(Path.Combine(save, "rep+persistentgamedata2.dat")).SequenceEqual(FakeSave(109, 54, 268)), "copia: progressi dello slot 1 nello slot 2");
			Assert(File.Exists(Path.Combine(save, "rep+gamestate2.dat")) && File.ReadAllText(Path.Combine(data, "eid", "save2.dat")) == "eid1", "copia: partita in corso e dati delle mod");
			Assert(!File.Exists(Path.Combine(data, "stageapi", "save2.dat")), "copia: tolti i dati delle mod che lo slot 1 non ha");
			var before = Saves.Backups().Single();
			Assert(before.Slot == 2 && before.Info.Achievements == 5 && !before.HasRun && before.Label.Contains("copia"), "prima della copia c'è il backup automatico dello slot 2");

			Saves.Restore(before, 2);
			Assert(File.ReadAllBytes(Path.Combine(save, "rep+persistentgamedata2.dat")).SequenceEqual(FakeSave(5, 0, 0)), "ripristino: lo slot 2 torna com'era");
			Assert(!File.Exists(Path.Combine(save, "rep+gamestate2.dat")) && !File.Exists(Path.Combine(data, "eid", "save2.dat")), "ripristino: niente partita né dati mod che il backup non ha");
			Assert(File.ReadAllText(Path.Combine(data, "stageapi", "save2.dat")) == "old2", "ripristino: dati delle mod del backup rimessi");
			Assert(Saves.Backups().Count == 2, "ripristino: prima salvato lo slot 2 che c'era");

			Directory.CreateDirectory(Path.Combine(Saves.DocsDir, "save_backups"));
			File.WriteAllBytes(Path.Combine(Saves.DocsDir, "save_backups", "20260101.rep+persistentgamedata1.dat"), FakeSave(10, 2, 7));
			File.WriteAllBytes(Path.Combine(Saves.DocsDir, "save_backups", "20260101.rep+persistentgamedata3.dat"), FakeSave(0, 0, 0));
			var game = Saves.Backups().Where(b => b.FromGame).ToList();
			Assert(game.Count == 1 && game[0].Slot == 1 && game[0].Info.Deaths == 2, "copie del gioco elencate, senza gli slot mai giocati");
			Saves.Restore(game[0], 3);
			Assert(File.ReadAllBytes(Path.Combine(save, "rep+persistentgamedata3.dat")).SequenceEqual(FakeSave(10, 2, 7)), "ripristino di una copia del gioco in uno slot vuoto");
			Saves.Delete(before);
			Assert(!Directory.Exists(before.Path), "backup eliminato");
		}

		static void TestUpdater(string t)
		{
			string Rel(string tag, string asset, bool pre = false) =>
				$"{{\"tag_name\":\"{tag}\",\"draft\":false,\"prerelease\":{(pre ? "true" : "false")},\"body\":\"n\",\"html_url\":\"p/{tag}\",\"assets\":[{{\"name\":\"{asset}\",\"browser_download_url\":\"u/{asset}\"}}]}}";
			var json = "[" + string.Join(",", Rel("v1.0.1", "bto-x-1.0.1.zip"), Rel("sync-v1.0", "BTO-Sync-1.0.zip"), Rel("sync-v2.1.0", "BTO-Sync-2.1.0.zip"),
				Rel("sync-v9.0.0", "BTO-Sync-9.0.0.zip", pre: true), Rel("sync-v2.0.3", "BTO-Sync-2.0.3.zip"), Rel("sync-v3.0.0", "altro.zip")) + "]";
			var r = Updater.Pick(json);
			Assert(r.VersionText == "2.1.0" && r.Url == "u/BTO-Sync-2.1.0.zip", "aggiornamento: la versione più alta del programma, senza prerelease né release delle mod");
			Assert(Updater.Pick("[]") == null && new Version(1, 0, 0) < r.Version, "aggiornamento: niente release = niente da fare");

			string app = Path.Combine(t, "app"), src = Path.Combine(t, "upd", "BTO-Sync"), zip = Path.Combine(t, "upd.zip");
			Directory.CreateDirectory(app); Directory.CreateDirectory(src);
			File.WriteAllText(Path.Combine(app, "BTO-Sync.exe"), "old exe"); File.WriteAllText(Path.Combine(app, "lib.dll"), "old dll");
			File.WriteAllText(Path.Combine(src, "BTO-Sync.exe"), "new exe"); File.WriteAllText(Path.Combine(src, "new.dll"), "new dll");
			System.IO.Compression.ZipFile.CreateFromDirectory(Path.Combine(t, "upd"), zip);
			Updater.Apply(zip, app);  // replacing the running exe is checked for real by updating an older build
			Assert(File.ReadAllText(Path.Combine(app, "BTO-Sync.exe")) == "new exe" && File.ReadAllText(Path.Combine(app, "new.dll")) == "new dll", "aggiornamento: file nuovi al loro posto");
			Assert(File.ReadAllText(Path.Combine(app, "BTO-Sync.exe.old")) == "old exe" && File.ReadAllText(Path.Combine(app, "lib.dll")) == "old dll", "aggiornamento: vecchio exe messo da parte, altri file lasciati");
			Updater.CleanOld(app);
			Assert(!File.Exists(Path.Combine(app, "BTO-Sync.exe.old")), "aggiornamento: .old tolti al riavvio");

			File.WriteAllText(Path.Combine(t, "rotto.zip"), "non è uno zip");
			bool failed = false; try { Updater.Apply(Path.Combine(t, "rotto.zip"), app); } catch { failed = true; }
			Assert(failed && File.ReadAllText(Path.Combine(app, "BTO-Sync.exe")) == "new exe", "aggiornamento rotto: il programma resta com'era");
		}

		public static int Run()
		{
			var t = Path.Combine(Path.GetTempPath(), "bto-sync-test-" + Guid.NewGuid().ToString("N"));
			try
			{
				string host = Path.Combine(t, "host", "mods"), me = Path.Combine(t, "me", "mods");
				Mod(host, "bto-x", "BTO X", "", "print(1)");
				Mod(host, "bto-y", "BTO Y", "", "print(2)");
				Mod(host, "wsmod_111", "Workshop Mod", "111", "print(3)");
				Mod(me, "bto-x", "BTO X", "", "print(1)"); File.WriteAllText(Path.Combine(me, "bto-x", "disable.it"), "");
				Mod(me, "bto-y", "BTO Y", "", "print(22)");
				Mod(me, "extra", "Extra", "", "print(4)");
				Mod(me, "old", "Old", "", "print(5)"); File.WriteAllText(Path.Combine(me, "old", "disable.it"), "");

				Assert(Core.ModHash(Path.Combine(host, "bto-x")) == Core.ModHash(Path.Combine(me, "bto-x")), "hash uguale con stessi file (disable.it escluso)");
				Assert(Core.ModHash(Path.Combine(host, "bto-y")) != Core.ModHash(Path.Combine(me, "bto-y")), "hash diverso con file diversi");
				Directory.CreateDirectory(Path.Combine(t, "lf")); Directory.CreateDirectory(Path.Combine(t, "crlf"));
				File.WriteAllText(Path.Combine(t, "lf", "main.lua"), "a\nb\n"); File.WriteAllText(Path.Combine(t, "crlf", "main.lua"), "a\r\nb\r\n");
				Assert(Core.ModHash(Path.Combine(t, "lf")) == Core.ModHash(Path.Combine(t, "crlf")), "hash uguale con fine riga CRLF o LF");

				var code = Core.ToCode(Core.ScanMods(host).Where(m => m.Enabled));
				var hostMods = Core.FromCode("`" + code + "`\n");
				Assert(hostMods.Count == 3, "il codice (anche tra backtick) contiene le 3 mod attive");
				bool bad = false; try { Core.FromCode("BTO1:xx"); } catch (FormatException) { bad = true; }
				Assert(bad, "un codice rovinato dà un errore leggibile");

				var local = Core.ScanMods(me);
				var plan = Core.MakePlan(hostMods, local, new Dictionary<string, string>());
				var kinds = string.Join(" ", plan.Actions.Select(a => a.Kind + ":" + (a.Local?.Folder ?? a.Host.folder)).OrderBy(s => s, StringComparer.Ordinal));
				Assert(kinds == "Disable:extra Download:bto-y Enable:bto-x Subscribe:wsmod_111", "piano: " + kinds);

				foreach (var a in plan.Actions.Where(a => a.Kind == ActionKind.Enable || a.Kind == ActionKind.Disable)) Core.SetEnabled(a.Local, a.Kind == ActionKind.Enable);
				Assert(!File.Exists(Path.Combine(me, "bto-x", "disable.it")), "bto-x riattivata");
				Assert(File.Exists(Path.Combine(me, "extra", "disable.it")), "extra disattivata");
				Assert(File.Exists(Path.Combine(me, "old", "disable.it")), "old resta disattivata");

				var missing = Core.ApplyProfile(new Profile { name = "t", keys = new List<string> { "bto-y", "nessuna" } }, Core.ScanMods(me));
				var after = Core.ScanMods(me).ToDictionary(m => m.Folder, m => m.Enabled);
				Assert(after["bto-y"] && !after["bto-x"] && !after["extra"] && !after["old"], "applica profilo: attive solo le sue mod");
				Assert(missing.Count == 1 && missing[0] == "nessuna", "applica profilo: segnala le mod non installate");

				// Workshop copy: named like Isaac does, keeps the disabled flag of the old copy
				File.WriteAllText(Path.Combine(host, "wsmod_111", "main.lua"), "print(33)");
				Mod(me, "wsmod_111", "Workshop Mod", "111", "print(3)"); File.WriteAllText(Path.Combine(me, "wsmod_111", "disable.it"), "");
				Core.InstallWorkshopCopy(Path.Combine(host, "wsmod_111"), "111", me);
				Assert(File.ReadAllText(Path.Combine(me, "wsmod_111", "main.lua")) == "print(33)", "copia Workshop installata come <directory>_<id>");
				Assert(File.Exists(Path.Combine(me, "wsmod_111", "disable.it")), "la copia Workshop resta disattivata se lo era");
				Core.RemoveWorkshopCopy("111", me);
				Assert(!Core.HasWorkshopCopy("111", me), "copia Workshop rimossa");
				Assert(Core.CleanDescription("[h1]Titolo[/h1]\r\n[img]http://x/y.png[/img]Testo [b]forte[/b] [url=http://a]link[/url]\n[list][*]uno[*]due[/list]")
					== "Titolo\nTesto forte link\n• uno• due", "descrizione Workshop ripulita dal BBCode");
				TestSaves(t);
				TestUpdater(t);
				Console.WriteLine("OK");
				return 0;
			}
			catch (Exception e) { Console.WriteLine(e.Message); return 1; }
			finally { try { Directory.Delete(t, true); } catch { } }
		}
	}
}
