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
				Console.WriteLine("OK");
				return 0;
			}
			catch (Exception e) { Console.WriteLine(e.Message); return 1; }
			finally { try { Directory.Delete(t, true); } catch { } }
		}
	}
}
