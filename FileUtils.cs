using System;
using System.IO;

namespace SketchfabRipper
{
	public class FileUtils
	{
		public static void MoveFile(string fileSource, string fileDest, string fileName = "")
		{
			try
			{
				if (!string.IsNullOrEmpty(fileName))
				{
					fileDest = $"{Path.GetDirectoryName(fileDest)}\\{fileName}";
				}
				if (!Directory.Exists(Path.GetDirectoryName(fileDest)))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(fileDest));
				}
				if (File.Exists(fileDest))
				{
					try
					{
						File.Delete(fileDest);
					}
					catch {}
				}
				File.Move(fileSource, fileDest);
			}
			catch {}
		}

		public static void CopyFile(string fileSource, string fileDest, string fileName = "")
		{
			try
			{
				if (!string.IsNullOrEmpty(fileName))
				{
					fileDest = $"{Path.GetDirectoryName(fileDest)}\\{fileName}";
				}
				if (!Directory.Exists(Path.GetDirectoryName(fileDest)))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(fileDest));
				}
				if (File.Exists(fileDest))
				{
					DeleteFile(fileDest);
				}
				File.Copy(fileSource, fileDest);
			}
			catch {}
		}

		public static void MoveDir(string dirSource, string dirDest)
		{
			try
			{
				if (!Directory.Exists(dirDest))
				{
					Directory.CreateDirectory(dirDest);
				}
				foreach (string file in Directory.GetFiles(dirSource))
				{
					File.Move(file, Path.Combine(dirDest, Path.GetFileName(file)));
				}
				foreach (string dir in Directory.GetDirectories(dirSource))
				{
					MoveDir(dir, Path.Combine(dirDest, Path.GetFileName(dir)));
				}
				DeleteDir(dirSource);
			}
			catch {}
		}

		public static void DeleteFile(string filePath)
		{
			try
			{
				File.Delete(filePath);
			}
			catch {}
		}

		public static void DeleteDir(string dirPath)
		{
			try
			{
				Directory.Delete(dirPath, true);
			}
			catch {}
		}

		public static void RecursiveCopy(string sourceDir, string targetDir)
		{
			try
			{
				Directory.CreateDirectory(targetDir);
				foreach (var file in Directory.GetFiles(sourceDir))
				{
					File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
				}
				foreach (var directory in Directory.GetDirectories(sourceDir))
				{
					RecursiveCopy(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
				}
			}
			catch {}
		}

		public static string GetRelativePath(string relativeTo, string path)
		{
			var relPath = Uri.UnescapeDataString(new Uri(relativeTo).MakeRelativeUri(new Uri(path)).ToString()).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if (relPath.Contains(Path.DirectorySeparatorChar.ToString()) == false)
			{
				relPath = $".{Path.DirectorySeparatorChar}{relPath}";
			}
			return relPath;
		}
	}
}
