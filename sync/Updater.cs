using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BtoSync
{
	public class AppRelease
	{
		public Version Version;
		public string Url, Notes, Page;
		public string VersionText => Version.ToString(3);
	}

	// Self-update from GitHub releases: tag "sync-vX.Y.Z" with the asset "BTO-Sync-X.Y.Z.zip" (the app files, in a folder or at the root).
	public static class Updater
	{
		public static Version Current => typeof(Updater).Assembly.GetName().Version;
		public static string AppDir => AppDomain.CurrentDomain.BaseDirectory;

		// Newest release of the app in the GitHub releases JSON, or null.
		public static AppRelease Pick(string releasesJson)
		{
			AppRelease best = null;
			foreach (Dictionary<string, object> rel in Core.Arr(new JavaScriptSerializer().DeserializeObject(releasesJson)))
			{
				var m = Regex.Match(rel["tag_name"] as string ?? "", @"^sync-v(\d+(\.\d+){1,2})$");
				if (!m.Success || true.Equals(rel["draft"]) || true.Equals(rel["prerelease"])) continue;
				var v = Version.Parse(m.Groups[1].Value);
				v = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
				var asset = Core.Arr(rel["assets"]).Cast<Dictionary<string, object>>().FirstOrDefault(a => Regex.IsMatch((string)a["name"], @"^BTO-Sync-[\d.]+\.zip$"));
				if (asset == null || (best != null && best.Version >= v)) continue;
				best = new AppRelease { Version = v, Url = (string)asset["browser_download_url"], Notes = rel["body"] as string ?? "", Page = rel["html_url"] as string };
			}
			return best;
		}

		public static async Task<AppRelease> Latest() => Pick(await Core.Http.GetStringAsync($"https://api.github.com/repos/{Core.Repo}/releases?per_page=100"));

		public static async Task<string> Download(AppRelease r)
		{
			var zip = Path.Combine(Path.GetTempPath(), $"BTO-Sync-{r.VersionText}-{Guid.NewGuid():N}.zip");
			File.WriteAllBytes(zip, await Core.Http.GetByteArrayAsync(r.Url));
			return zip;
		}

		// Puts the zip's files in appDir. Windows lets a running exe/dll be renamed but not overwritten, so each old file
		// becomes "<name>.old" (deleted at the next start); on any error the old files are put back.
		public static void Apply(string zip, string appDir)
		{
			var tmp = Path.Combine(Path.GetTempPath(), "bto-sync-update-" + Guid.NewGuid().ToString("N"));
			var moved = new List<string>();
			var added = new List<string>();
			try
			{
				ZipFile.ExtractToDirectory(zip, tmp);
				var exe = Directory.GetFiles(tmp, "BTO-Sync.exe", SearchOption.AllDirectories).FirstOrDefault()
					?? throw new InvalidDataException("lo zip non contiene BTO-Sync.exe");
				foreach (var f in Directory.GetFiles(Path.GetDirectoryName(exe)))
				{
					var dst = Path.Combine(appDir, Path.GetFileName(f));
					if (File.Exists(dst))
					{
						File.Delete(dst + ".old");
						File.Move(dst, dst + ".old");
						moved.Add(dst);
					}
					File.Copy(f, dst);
					added.Add(dst);
				}
			}
			catch
			{
				foreach (var f in added) try { File.Delete(f); } catch { }
				foreach (var f in moved) try { File.Move(f + ".old", f); } catch { }
				throw;
			}
			finally { try { Directory.Delete(tmp, true); } catch { } }
		}

		// Leftovers of the previous version; still locked if it has not exited yet, then they go next time.
		public static void CleanOld(string appDir)
		{
			foreach (var f in Directory.GetFiles(appDir, "*.old")) try { File.Delete(f); } catch { }
		}

		public static void Restart() => Process.Start(Path.Combine(AppDir, "BTO-Sync.exe"));
	}
}
