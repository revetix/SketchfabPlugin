using System;
using System.IO;

namespace SketchfabRipper
{
	class Logger
	{
		internal enum ErrorLevel
		{
			Debug = 0,
			Info = 1,
			Warning = 2,
			Error = 3,
			Critical = 4
		}

		public static void Log(string message, Logger.ErrorLevel errorLevel, string file = "\\logs\\log.txt", bool addLevel = true, string exception = null)
		{
			Directory.CreateDirectory(Environment.CurrentDirectory + Path.GetDirectoryName(file));
			string errorLevelString = null;
			if (addLevel)
			{
				errorLevelString = $"[{errorLevel}] ";
			}

			//var options = new Options();
			//if (errorLevel > options.LogLevel)
			{
				File.AppendAllText(Environment.CurrentDirectory + file, $"{errorLevelString}{message}{Environment.NewLine}");
				if (exception != null)
				{
					File.AppendAllText(Environment.CurrentDirectory + file, $"{exception}{Environment.NewLine}");
				}
			}
		}
	}
}
