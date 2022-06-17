using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json.Linq;
using SketchfabRipper;
//using TelegramLib;

namespace SketchfabPlugin
{
	class SketchfabDL
	{
		public static bool isPublicBuild = false;

		public static bool decryptionFailed;
		public static bool hasConfigData;
		public static bool isBatch;
		public static bool isFirstRun;
		public static bool isIndexFix;
		public static bool isLocked;
		public static bool needsToRetry;
		public static bool newConfSet;

		public static double progress;

		public static int convertLargeFiles;
		//public static int pid_binzDumper;
		public static int pid_blender;
		public static int pid_fbxConv;
		//public static int pid_nginx;
		public static int pid_sevenZip;
		public static int pid_zsUpload;
		public static int retryNum;

		public static string messageBoxResult;
		public static string modelName;
		public static string newConf;
		public static string session;
		public static string sessionID;
		// todo: change to enum
		public static string urlType;

		private static bool hasAskedToRetry;
		private static bool hasAskedToSkip;
		private static bool nameExistsInURL = true;
		private static bool retryAll;
		private static bool skipAll;
		
		private static string modelAuthor;
		private static string modelDir;

		private static int progressAnimCount;
		private static int progressBlenderScripts;
		public static int progressMeshCount;
		private static int progressTris;

		private static BackgroundWorker bw;

		private static Dictionary<string, string> options;

		private static void blenderOutputHandler(object sender, DataReceivedEventArgs e)
		{
			try
			{
				if (e.Data != null)
				{
					Logger.Log(e.Data, Logger.ErrorLevel.Info, "\\logs\\log-blender.txt", false);
				}
				if (e.Data != null && e.Data.Contains("Error executing Python script from command-line"))
				{
					Logger.Log("Detected Python exception, retrying with IndexError fix enabled...", Logger.ErrorLevel.Warning);
					needsToRetry = true;
				}
				else if (e.Data != null && e.Data.StartsWith("Triangles:") && int.Parse(e.Data.Replace("Triangles: ", "")) > 0)
				{
					if (options["ConversionMode"] == "Static")
					{
						bw.ReportProgress((int)Math.Round(progress += 45.0 * ((double)int.Parse(Regex.Match(e.Data, "\\d+").Value) / progressTris)));
					}
					else
					{
						if (options["ConversionMode"] == "Rigged")
						{
							bw.ReportProgress((int)Math.Round(progress += 22.5 / progressMeshCount));
						}
						else
						{
							bw.ReportProgress((int)Math.Round(progress += 11.25 / progressMeshCount));
						}
					}
				}
				else if (e.Data != null && e.Data.Contains(" vert: "))
				{
					if (!e.Data.Contains(" vert: 0"))
					{
						if (options["ConversionMode"] == "Rigged")
						{
							bw.ReportProgress((int)Math.Round(progress += 22.5 / progressMeshCount));
						}
						else
						{
							bw.ReportProgress((int)Math.Round(progress += 11.25 / progressMeshCount));
						}
					}
				}
				else if (e.Data != null && e.Data.StartsWith("Progress"))
				{
					if (e.Data.StartsWith("Progress: "))
					{
						bw.ReportProgress(-1, e.Data.Substring(10));
					}
					else if (e.Data.StartsWith("Progress : "))
					{
						bw.ReportProgress((int)Math.Round(progress += 15.0 / (progressBlenderScripts + 2)), e.Data.Substring(11));
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to parse Blender output:", Logger.ErrorLevel.Error, exception: ex.ToString());
			}
		}

		private static void ExportAnimations(string modelPath, string blenderScriptExp, BackgroundWorker bw, CancellationToken token)
		{
			Logger.Log("Exporting animations...", Logger.ErrorLevel.Info);
			modelDir = $"{Environment.CurrentDirectory}\\downloads\\{modelPath}";
			if (Directory.Exists($"{modelDir}\\animations\\"))
			{
				try
				{
					foreach (var file in new DirectoryInfo($"{modelDir}\\animations\\").GetFiles())
					{
						try
						{
							file.CopyTo($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\{file.Name}");
						}
						catch {}
					}

					var blender = new Process();
					blender.StartInfo.Arguments = $"blank.blend -g noaudio -noaudio -noglsl -y -b -P _sfTemp\\{session}\\_sketchfab.py";
					blender.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\blender\\blender.exe";
					blender.StartInfo.UseShellExecute = false;
					blender.StartInfo.RedirectStandardOutput = true;
					blender.StartInfo.RedirectStandardError = true;
					blender.OutputDataReceived += blenderOutputHandler;
					blender.StartInfo.WorkingDirectory = $"{Environment.CurrentDirectory}\\tools\\blender\\";
					blender.StartInfo.CreateNoWindow = true;
					blender.Start();
					pid_blender = blender.Id;
					blender.BeginOutputReadLine();
					blender.BeginErrorReadLine();
					blender.WaitForExit();

					if (token.IsCancellationRequested)
					{
						bw.ReportProgress(0, "Download cancelled.");
						return;
					}

					Directory.CreateDirectory($"{modelDir}\\animations_action\\");

					foreach (var file in new DirectoryInfo($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\file.osgjs_files\\").GetFiles())
					{
						byte[] fileData = File.ReadAllBytes(file.FullName);
						if (fileData.Length == 0)
						{
							File.Delete(file.FullName);
							continue;
						}
						try
						{
							file.CopyTo($@"{$"{modelDir}\\animations_action\\"}\{file.Name}");
						}
						catch {}
					}

					PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender\\_export-blend.py", $"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_export-blend.py");
					PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender\\_export-fbx.py", $"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_export-fbx.py");

					foreach (var file in new DirectoryInfo($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\file.osgjs_files\\").GetFiles())
					{
						try
						{
							bw.ReportProgress(-1, "Applying animations...");
							Logger.Log($"Applying animation \"{file.Name}\"", Logger.ErrorLevel.Info);
							//BlenderScripts.WriteAnimatedPartial(file.Name);
							File.WriteAllText($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_sketchfabAnimImport.py", BlenderScripts.RiggedAnimated(1, false, true, $"./_sfTemp/{session}/file.osgjs_files/{file.Name}"));
							blender.StartInfo.Arguments = $"blank.blend -g noaudio -noaudio -noglsl -y -b -P _sfTemp\\{session}\\_sketchfab.py -P _sfTemp\\{session}\\_sketchfabAnimImport.py -P _sfTemp\\{session}\\_export-blend.py -P _sfTemp\\{session}\\_export-fbx.py";
							blender.Start();
							blender.WaitForExit();

							if (token.IsCancellationRequested)
							{
								bw.ReportProgress(0, "Download cancelled.");
								return;
							}

							FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\model.fbx", $"{modelDir}\\{Path.GetFileNameWithoutExtension(file.Name)}.fbx");

							if (options["OutputFormat"] == "FBXBinary")
							{
								Logger.Log($"Running FBX converter for \"{file.Name}\"...", Logger.ErrorLevel.Info);
								var fbxconv = new Process();
								fbxconv.StartInfo.Arguments = $"\"{modelDir}\\{Path.GetFileNameWithoutExtension(file.Name)}.fbx\" \"{modelDir}\\{Path.GetFileNameWithoutExtension(file.Name)}.binary.fbx";
								fbxconv.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\FbxConverter.exe";
								fbxconv.StartInfo.UseShellExecute = false;
								fbxconv.StartInfo.CreateNoWindow = true;
								fbxconv.Start();
								pid_fbxConv = fbxconv.Id;
								fbxconv.WaitForExit();
								if (token.IsCancellationRequested)
								{
									bw.ReportProgress(0, "Download cancelled.");
									return;
								}
								FileUtils.DeleteFile($"{modelDir}\\{Path.GetFileNameWithoutExtension(file.Name)}.fbx");
								FileUtils.MoveFile($"{modelDir}\\{Path.GetFileNameWithoutExtension(file.Name)}.binary.fbx", $"{modelDir}\\{Path.GetFileNameWithoutExtension(file.Name)}.fbx");
							}
							FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\model.blend", $"{modelDir}\\{Path.GetFileNameWithoutExtension(file.Name)}.blend");
							Logger.Log($"Exported \"{file.Name}\"", Logger.ErrorLevel.Info);
							bw.ReportProgress((int)Math.Round(progress += 27.5 / progressAnimCount));
						}
						catch
						{
						}
					}
					Logger.Log("Animation export complete.", Logger.ErrorLevel.Info);
					bw.ReportProgress((int)Math.Round(progress));
				}
				catch
				{
				}
			}
		}

		public static void PrepareScriptFile(string src, string dest, string tempPath = null)
		{
			var fileData = File.ReadAllText(src);
			if (tempPath == null)
			{
				fileData = fileData.Replace("{tempPath}", $"_sfTemp/{session}");
				fileData = fileData.Replace("{tempPath2}", $"_sfTemp\\\\{session}");
			}
			else
			{
				fileData = fileData.Replace("{tempPath}", tempPath);
				fileData = fileData.Replace("{tempPath2}", tempPath.Replace("/", "\\\\"));
			}
			File.WriteAllText(dest, fileData);
		}

		public static string LockedModelRequest(string url)
		{
			CookieContainer cookieContainer = new CookieContainer();
			cookieContainer.Add(new Cookie("sb_sessionid", sessionID, "/", "sketchfab.com"));
			HttpWebRequest httpWebRequest = WebRequest.CreateHttp(url);
			httpWebRequest.Method = "GET";
			httpWebRequest.CookieContainer = cookieContainer;
			httpWebRequest.Timeout = 4000;
			var responseStream = httpWebRequest.GetResponse().GetResponseStream();
			return new StreamReader(responseStream, Encoding.UTF8).ReadToEnd();
		}

		private static string UnlockModel(string id)
		{
			bw.ReportProgress(-1, "TextPrompt:Locked model|Enter password|Continue");
			var pass = messageBoxResult;
			HttpWebRequest httpWebRequest = WebRequest.CreateHttp($"https://sketchfab.com/i/models/{id}/password");
			httpWebRequest.Method = "POST";
			httpWebRequest.ContentType = "application/json";
			httpWebRequest.Timeout = 4000;
			byte[] bytes = Encoding.UTF8.GetBytes($"{{\"password\":\"{pass}\"}}");
			using (Stream requestStream = httpWebRequest.GetRequestStream())
			{
				requestStream.Write(bytes, 0, bytes.Length);
			}
			sessionID = httpWebRequest.GetResponse().Headers[HttpResponseHeader.SetCookie];
			sessionID = sessionID.Remove(sessionID.IndexOf(";"));
			sessionID = sessionID.Substring(sessionID.IndexOf("=") + 1);
			isLocked = true;
			return LockedModelRequest($"https://sketchfab.com/i/models/{id}");
		}

		public static void Download(string url, Dictionary<string, string> options, BackgroundWorker bw, CancellationToken token, bool runTwice = false, string data = null)
		{
			isLocked = false;
			session = Guid.NewGuid().ToString();
			progress = 0;
			try
			{
				SketchfabDL.bw = bw;
				SketchfabDL.options = options;
				decryptionFailed = false;

				if (url.Length == 0)
				{
					if (!bool.Parse(options["SilentMode"]))
					{
						bw.ReportProgress(-1, "MessageBox:Please enter a valid URL.");
					}
					return;
				}

				try
				{
					int startPoseMode = int.Parse(options["SendToStartPose"]);
					if (runTwice)
					{
						startPoseMode = Convert.ToInt32(isFirstRun);
					}

					string modelFile;
					string modelPath;
					var webClient = new WebClient();

					bw.ReportProgress(-1, "Processing...");
					if (url.StartsWith("https://sketchfab.com/models/"))
					{
						url = url.Replace("/models/", "/3d-models/");
					}

					try
					{
						Regex URLNameRegex = new Regex("3d-models/(.*)-");
						Match URLNameMatch = URLNameRegex.Match(url);
						if (!URLNameMatch.Success)
						{
							nameExistsInURL = false;
						}
						else
						{
							nameExistsInURL = true;
						}
					}
					catch (Exception e)
					{
						if (!bool.Parse(options["SilentMode"]))
						{
							bw.ReportProgress(-1, $"MessageBox:{e}");
						}
					}

					string modelID = Regex.Match(url, "3d-models/.*-(.*)").Groups[1].Value;
					string jsonData;
					if (nameExistsInURL)
					{
						try
						{
							jsonData = webClient.DownloadString($"https://sketchfab.com/i/models/{modelID}");
						}
						catch (WebException)
						{
							jsonData = UnlockModel(modelID);
						}
						JObject jObject = JObject.Parse(jsonData);
						modelPath = jObject["slug"].ToString();
						modelFile = jObject["slug"].ToString();
						if (string.IsNullOrEmpty(modelPath.Replace("-", "").Replace(".", "").Replace("_", "")))
						{
							modelPath = jObject["name"].ToString();
							modelFile = jObject["name"].ToString();
						}
					}
					else
					{
						modelID = Regex.Replace(url, ".*/(?:.*-)?", "");
						try
						{
							jsonData = webClient.DownloadString($"https://sketchfab.com/i/models/{modelID}");
						}
						catch (WebException)
						{
							jsonData = UnlockModel(modelID);
						}
						JObject jObject = JObject.Parse(jsonData);
						modelPath = jObject["slug"].ToString();
						modelFile = jObject["slug"].ToString();
						if (string.IsNullOrEmpty(modelPath.Replace("-", "").Replace(".", "").Replace("_", "")))
						{
							modelPath = jObject["name"].ToString();
							modelFile = jObject["name"].ToString();
						}
					}

					JObject modelData = JObject.Parse(jsonData);
					modelAuthor = modelData["user"]["username"].ToString();
					modelName = modelData["name"].ToString();
					progressTris = int.Parse(modelData["faceCount"].ToString());
					try
					{
						progressAnimCount = int.Parse(modelData["animationCount"].ToString());
					}
					catch {}

					if (bool.Parse(options["AppendUniqueIDs"]))
					{
						modelPath = $"{modelPath}_{modelID}";
					}

					foreach (char c in Path.GetInvalidPathChars())
					{
						modelPath = modelPath.Replace(c.ToString(), "_");
					}

					try
					{
						var wc = new WebClient();
						string htmlData;
						if (!isLocked) htmlData = wc.DownloadString($"https://sketchfab.com/models/{modelID}/embed");
						else htmlData = LockedModelRequest($"https://sketchfab.com/models/{modelID}/embed");
						htmlData = Regex.Match(htmlData, "(?<=js-dom-data-prefetched-data\"><!--).*(?=--></div>)").Value;
						htmlData = WebUtility.HtmlDecode(htmlData);
						htmlData = JObject.Parse(htmlData)[$"/i/models/{modelID}"].ToString();
						DumpResources.viewerData = JObject.Parse(htmlData);
					}
					catch {}

					if (Directory.Exists($"{Environment.CurrentDirectory}\\downloads\\{modelPath}") && !isIndexFix && isFirstRun)
					{
						if (skipAll || bool.Parse(options["SilentMode"]))
						{
							return;
						}

						bw.ReportProgress(-1, $"MessageBoxYesNo:Folder \"{modelPath}\" already exists. Download again?");
						if (messageBoxResult == "MessageBoxResult_No")
						{
							if (isBatch)
							{
								if (!hasAskedToSkip)
								{
									hasAskedToSkip = true;
									bw.ReportProgress(-1, "MessageBox:To skip all existing models in the current queue, enable \"Silent mode\" from the Options window.");
								}
								return;
							}
							if (urlType == "Artist" || urlType == "Collection")
							{
								if (!hasAskedToSkip)
								{
									hasAskedToSkip = true;
									bw.ReportProgress(-1, "MessageBoxYesNo:Skip all existing models in the current queue?");
									if (messageBoxResult == "MessageBoxResult_Yes")
									{
										skipAll = true;
									}
								}
								return;
							}
							if (!isBatch)
							{
								bw.ReportProgress(0, "Download cancelled.");
							}
							return;
						}
					}
					else if (Directory.Exists($"{Environment.CurrentDirectory}\\downloads\\{modelAuthor}\\{modelPath}") && !isIndexFix && isFirstRun)
					{
						if (skipAll || bool.Parse(options["SilentMode"]))
						{
							return;
						}
						bw.ReportProgress(-1, $"MessageBoxYesNo:Folder \"{modelAuthor}\\{modelPath}\" already exists. Download again?");
						if (messageBoxResult == "MessageBoxResult_No")
						{
							if (isBatch)
							{
								if (!hasAskedToSkip)
								{
									hasAskedToSkip = true;
									bw.ReportProgress(-1, "MessageBox:To skip all existing models in the current queue, enable \"Silent mode\" from the Options window.");
								}
								return;
							}
							if (urlType == "Artist" || urlType == "Collection")
							{
								if (!hasAskedToSkip)
								{
									hasAskedToSkip = true;
									bw.ReportProgress(-1, "MessageBoxYesNo:Skip all existing models in the current queue?");
									if (messageBoxResult == "MessageBoxResult_Yes")
									{
										skipAll = true;
									}
								}
								return;
							}
							if (!isBatch)
							{
								bw.ReportProgress(0, "Download cancelled.");
							}
							return;
						}
					}

					if (!isIndexFix && isFirstRun)
					{
						bw.ReportProgress(-1, "Downloading model...");
						if (urlType == "Artist" || urlType == "Collection")
						{
							bw.ReportProgress(-1, $"Downloading \"{Regex.Unescape(modelName)}\"...");
						}

						switch (options["SFAuthType"])
						{
							case "AccessToken":
								DumpResources.SketchfabDumper(modelID, options, bw, token, options["SFAccessToken"]);
								break;
							case "SessionID":
								DumpResources.SketchfabDumper(modelID, options, bw, token, sfSessionID: options["SFSessionID"]);
								break;
							default:
								DumpResources.SketchfabDumper(modelID, options, bw, token);
								break;
						}

						if (token.IsCancellationRequested || decryptionFailed)
						{
							bw.ReportProgress(0, "Download cancelled.");
							return;
						}
					}

					modelDir = $"{Environment.CurrentDirectory}\\downloads\\{modelPath}";
					string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
					foreach (char c in invalidChars)
					{
						modelFile = modelFile.Replace(c, '_');
					}

					if (bool.Parse(options["EnableConversion"]))
					{
						bw.ReportProgress(-1, "Preparing files for conversion...");

						if (new FileInfo($"{modelDir}\\file.osgjs").Length > 20000000)
						{
							if (convertLargeFiles == 0)
							{
								if (!bool.Parse(options["SilentMode"]))
								{
									bw.ReportProgress(-1, "MessageBoxYesNo:An .osgjs file larger than 20 MB has been detected. Models with large .osgjs files may take a long time to process - some files over 50 MB can take hours. Convert anyway?");
									if (messageBoxResult == "MessageBoxResult_Yes")
									{
										convertLargeFiles = 2;
									}
									else
									{
										convertLargeFiles = 1;
									}
								}
								else
								{
									convertLargeFiles = 2;
								}
							}

							if (convertLargeFiles == 1)
							{
								return;
							}
						}

						FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\");
						FileUtils.CopyFile($"{modelDir}\\file.osgjs", $"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\file.osgjs");
						FileUtils.CopyFile($"{modelDir}\\model_file.bin", $"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\model_file.bin");
						FileUtils.CopyFile($"{modelDir}\\model_file_wireframe.bin", $"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\model_file_wireframe.bin");

						/*try
						{
							File.WriteAllText($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\dummy.html", "");
						}
						catch (Exception)
						{
						}*/

						FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\vertexNormalData");
						try
						{
							Directory.CreateDirectory($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\vertexNormalData");
						}
						catch
						{
						}

						var blender = new Process();
						string blenderScriptImp = "_sketchfab.py";
						string blenderScriptExp = null;
						string blenderModelExt = null;

						switch (options["ConversionMode"])
						{
							case "Static":
								File.WriteAllText($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_sketchfab.py", BlenderScripts.Static(startPoseMode, isIndexFix, $"_sfTemp/{session}"));
								break;
							case "Rigged":
								File.WriteAllText($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_sketchfab.py", BlenderScripts.RiggedAnimated(startPoseMode, isIndexFix, fileName: $"./_sfTemp/{session}/file.osgjs"));
								break;
							case "Animated":
								File.WriteAllText($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_sketchfab.py", BlenderScripts.RiggedAnimated(1, isIndexFix, true, $"./_sfTemp/{session}/file.osgjs"));
								break;
						}

						PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender\\calcVertexNormals.js", $"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\calcVertexNormals.js");
						PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender\\_export-blend.py", $"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_export-blend.py");
						PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender\\_export-fbx.py", $"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_export-fbx.py");
						PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender\\_export-obj.py", $"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_export-obj.py");

						if (!bool.Parse(options["LegacyMode"]))
						{
							switch (options["OutputFormat"])
							{
								case "FBX":
								case "FBXBinary":
								case "glTF":
									blenderModelExt = ".fbx";
									break;
								case "OBJ":
									if (bool.Parse(options["AutoTexture"]))
									{
										blenderModelExt = ".fbx";
										break;
									}
									blenderScriptImp = "_sketchfab.py";
									blenderModelExt = ".obj";
									break;
								case "None":

									blenderModelExt = ".blend";
									break;
							}
							blenderScriptExp = "_export-blend.py";
						}
						else
						{
							switch (options["OutputFormat"])
							{
								case "FBX":
									blenderScriptExp = "_export-fbx.py";
									blenderModelExt = ".fbx";
									break;
								case "FBXBinary":
									blenderScriptExp = "_export-fbx.py";
									blenderModelExt = ".fbx";
									break;
								case "glTF":
									blenderScriptExp = "_export-fbx.py";
									blenderModelExt = ".fbx";
									break;
								case "OBJ":
									if (bool.Parse(options["AutoTexture"]))
									{
										blenderScriptExp = "_export-fbx.py";
										blenderModelExt = ".fbx";
										break;
									}
									blenderScriptImp = "_sketchfab.py";
									blenderScriptExp = "_export-obj.py";
									blenderModelExt = ".obj";
									break;
								case "None":
									blenderScriptExp = "_export-blend.py";
									blenderModelExt = ".blend";
									break;
							}
						}

						if (startPoseMode == 0 && options["ConversionMode"] != "Animated")
						{
							if (options["ConversionMode"] == "Static")
							{
								File.WriteAllText($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_sketchfab.py", BlenderScripts.Static(0, isIndexFix, $"_sfTemp/{session}"));
							}
							if (options["ConversionMode"] == "Rigged")
							{
								File.WriteAllText($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_sketchfab.py", BlenderScripts.Rig(0, isIndexFix, $"_sfTemp/{session}"));
							}
						}

						if (bool.Parse(options["LegacyMode"]) && bool.Parse(options["SaveBlend"]))
						{
							blenderScriptExp += $" -P _sfTemp\\{session}\\_export-blend.py";
						}

						if (options["ConversionMode"] == "Static" && File.Exists($"{Environment.CurrentDirectory}\\tools\\blender\\.blender\\scripts\\newGameLib\\meshLib.py"))
						{
							FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\tools\\blender\\.blender\\scripts\\newGameLib");
							FileUtils.RecursiveCopy($"{Environment.CurrentDirectory}\\tools\\blender\\.blender\\scripts\\newGameLib_new", $"{Environment.CurrentDirectory}\\tools\\blender\\.blender\\scripts\\newGameLib");
						}
						else if (options["ConversionMode"] != "Static" && !File.Exists($"{Environment.CurrentDirectory}\\tools\\blender\\.blender\\scripts\\newGameLib\\meshLib.py"))
						{
							FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\tools\\blender\\.blender\\scripts\\newGameLib");
							FileUtils.RecursiveCopy($"{Environment.CurrentDirectory}\\tools\\blender\\.blender\\scripts\\newGameLib_old", $"{Environment.CurrentDirectory}\\tools\\blender\\.blender\\scripts\\newGameLib");
						}

						bw.ReportProgress(-1, "Converting model...");
						if (urlType == "Artist" || urlType == "Collection")
						{
							bw.ReportProgress(-1, $"Converting \"{Regex.Unescape(modelName)}\"...");
						}

						if (!Directory.Exists($"{modelDir}\\animations") && options["ConversionMode"] == "Animated")
						{
							File.WriteAllText($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\_sketchfab.py", BlenderScripts.Rig(0, isIndexFix, $"_sfTemp/{session}"));
						}

						if (options["ConversionMode"] != "Animated" || !Directory.Exists($"{modelDir}\\animations\\"))
						{
							progress = 25.0;
							bw.ReportProgress((int)Math.Round(progress));
							blender.StartInfo.Arguments = $"blank.blend -g noaudio -noaudio -noglsl -y -b -P _sfTemp\\{session}\\{blenderScriptImp} -P _sfTemp\\{session}\\{blenderScriptExp}";
							blender.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\blender\\blender.exe";
							blender.StartInfo.EnvironmentVariables["PythonPath"] = $"{Environment.CurrentDirectory}\\tools\\blender\\PYTHON26;{Environment.CurrentDirectory}\\tools\\blender\\PYTHON26\\DLLs;{Environment.CurrentDirectory}\\tools\\blender\\PYTHON26\\LIB;{Environment.CurrentDirectory}\\tools\\blender\\PYTHON26\\LIB\\LIB-TK";
							blender.StartInfo.UseShellExecute = false;
							blender.StartInfo.RedirectStandardOutput = true;
							blender.StartInfo.RedirectStandardError = true;
							blender.EnableRaisingEvents = true;
							blender.OutputDataReceived += blenderOutputHandler;
							blender.ErrorDataReceived += blenderOutputHandler;
							blender.StartInfo.WorkingDirectory = $"{Environment.CurrentDirectory}\\tools\\blender\\";
							blender.StartInfo.CreateNoWindow = true;
							blender.Start();
							pid_blender = blender.Id;
							blender.BeginOutputReadLine();
							blender.BeginErrorReadLine();
							blender.WaitForExit();

							if (blender.ExitCode != 0 && token.IsCancellationRequested)
							{
								bw.ReportProgress(0, "Download cancelled.");
								return;
							}

							if (blender.ExitCode != 0)
							{
								if (!bool.Parse(options["SilentMode"]))
								{
									bw.ReportProgress(-1, "MessageBox:Blender crashed or was manually terminated. Skipping conversion...");
								}
								bw.ReportProgress(-1, $"Conversion failed: {Regex.Unescape(modelName)}");
								return;
							}
						}
						else
						{
							ExportAnimations(modelPath, blenderScriptExp, bw, token);
							if (token.IsCancellationRequested)
							{
								bw.ReportProgress(0, "Download cancelled.");
								return;
							}
						}

						if (!isIndexFix && needsToRetry)
						{
							return;
						}

						FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\model{blenderModelExt}", $"{modelDir}\\{modelFile}{blenderModelExt}");
						FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\model.blend", $"{modelDir}\\{modelFile}.blend");

						if (bool.Parse(options["LegacyMode"]) && (options["OutputFormat"] == "FBXBinary" || (bool.Parse(options["AutoTexture"]) && options["OutputFormat"] == "OBJ") || options["OutputFormat"] == "glTF"))
						{
							bw.ReportProgress(-1, "Writing FBX (Binary)...");
							var fbxconv = new Process();
							fbxconv.StartInfo.Arguments = $"\"{modelDir}\\{modelFile}.fbx\" \"{modelDir}\\{modelFile}.binary.fbx";
							fbxconv.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\FbxConverter.exe";
							fbxconv.StartInfo.UseShellExecute = false;
							fbxconv.StartInfo.CreateNoWindow = true;
							fbxconv.Start();
							pid_fbxConv = fbxconv.Id;
							fbxconv.WaitForExit();

							if (fbxconv.ExitCode != 0 && token.IsCancellationRequested)
							{
								bw.ReportProgress(0, "Download cancelled.");
								return;
							}

							progress += 10.0;
							bw.ReportProgress((int)Math.Round(progress));
							FileUtils.DeleteFile($"{modelDir}\\{modelFile}.fbx");
							FileUtils.MoveFile($"{modelDir}\\{modelFile}.binary.fbx", $"{modelDir}\\{modelFile}.fbx");
						}

						if (options["OutputFormat"] == "FBXBinary" || options["OutputFormat"] == "OBJ" || options["OutputFormat"] == "None" || options["OutputFormat"] == "glTF")
						{
							FileUtils.CopyFile($"{modelDir}\\{modelFile}.fbx", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\model.fbx");
							FileUtils.CopyFile($"{modelDir}\\{modelFile}.blend", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\model.blend");
							FileUtils.CopyFile($"{modelDir}\\materialInfo.txt", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\materialInfo.txt");
							FileUtils.MoveDir($"{modelDir}\\textures", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\textures");
							FileUtils.MoveDir($"{modelDir}\\vertexColourData", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\vertexColourData");
							FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\vertexNormalData");
							FileUtils.MoveDir($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\vertexNormalData", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\vertexNormalData");

							PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_convertBlend.py", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\_convertBlend.py");
							PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_saveglTF.py", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\_saveglTF.py");
							PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_saveglTFQuads.py", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\_saveglTFQuads.py");
							PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_saveOBJ.py", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\_saveOBJ.py");
							PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_saveOBJQuads.py", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\_saveOBJQuads.py");
							PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_saveBlend.py", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\_saveBlend.py");
							PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_saveBlendQuads.py", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\_saveBlendQuads.py");
							PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTex.py", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\_sfTex.py");
							PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfVertCol.py", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\_sfVertCol.py");
							PrepareScriptFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfVertNormals.py", $"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\_sfVertNormals.py");

							string blenderExt = null;
							string blenderScriptExpQ = null;
							switch (options["OutputFormat"])
							{
								case "FBX":
								case "FBXBinary":
									blenderScriptExp = "_saveFBX.py";
									blenderScriptExpQ = "_saveFBXQuads.py";
									blenderExt = ".fbx";
									break;
								case "glTF":
									blenderScriptExp = "_saveglTF.py";
									blenderScriptExpQ = "_saveglTFQuads.py";
									blenderExt = ".gltf";
									break;
								case "OBJ":
									blenderScriptExp = "_saveOBJ.py";
									blenderScriptExpQ = "_saveOBJQuads.py";
									blenderExt = ".obj";
									break;
								case "None":
									blenderScriptExp = "_saveBlend.py";
									blenderScriptExpQ = "_saveBlendQuads.py";
									break;
							}

							if (bool.Parse(options["SaveBlend"]))
							{
								blenderScriptExp += $" -P _sfTemp\\{session}\\_saveBlend.py";
								blenderScriptExpQ += $" -P _sfTemp\\{session}\\_saveBlendQuads.py";
							}

							var blenderNew = new Process();
							blenderNew.StartInfo.Arguments = $"blank.blend -y -b -P _sfTemp\\{session}\\_sfVertCol.py";
							if (options["ConversionMode"] == "Static")
							{
								blenderNew.StartInfo.Arguments += $" -P _sfTemp\\{session}\\_sfVertNormals.py";
								if (options["ConversionMode"] != "Static")
								{
									blenderNew.StartInfo.Arguments += $" -P _sfTemp\\{session}\\_sfVertNormalsFix.py";
								}
							}
							blenderNew.StartInfo.Arguments += $" -P _sfTemp\\{session}\\_sfTex.py -P _sfTemp\\{session}\\{blenderScriptExp}";
							BlenderScripts.saveFBX(options["ConversionMode"], false, $"_sfTemp/{session}");
							if (bool.Parse(options["ConvertToQuads"]))
							{
								BlenderScripts.saveFBX(options["ConversionMode"], true, $"_sfTemp/{session}");
								blenderNew.StartInfo.Arguments += $" -P _sfTemp\\{session}\\_sfQuads.py -P _sfTemp\\{session}\\{blenderScriptExpQ}";
							}
							blenderNew.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\blender-292\\blender.exe";
							blenderNew.StartInfo.UseShellExecute = false;
							blenderNew.StartInfo.WorkingDirectory = $"{Environment.CurrentDirectory}\\tools\\blender-292\\";
							blenderNew.StartInfo.CreateNoWindow = true;
							blenderNew.StartInfo.RedirectStandardOutput = true;
							blenderNew.StartInfo.RedirectStandardError = true;
							blenderNew.EnableRaisingEvents = true;
							blenderNew.OutputDataReceived += blenderOutputHandler;
							blenderNew.ErrorDataReceived += blenderOutputHandler;
							progressBlenderScripts = Regex.Matches(blenderNew.StartInfo.Arguments, "\\.py").Count;

							if (!bool.Parse(options["LegacyMode"]))
							{
								bw.ReportProgress(-1, "Importing model...");
								var blendToFbx = new Process();
								blendToFbx.StartInfo.Arguments = $"blank.blend -b -P _sfTemp\\{session}\\_convertBlend.py";
								blendToFbx.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\blender-292\\blender.exe";
								blendToFbx.StartInfo.UseShellExecute = false;
								blendToFbx.StartInfo.WorkingDirectory = $"{Environment.CurrentDirectory}\\tools\\blender-292\\";
								blendToFbx.StartInfo.CreateNoWindow = true;
								blendToFbx.Start();
								pid_fbxConv = blendToFbx.Id;
								blendToFbx.WaitForExit();
								progress += 10.0;
								bw.ReportProgress((int)Math.Round(progress));
							}

							if (!bool.Parse(options["AutoTexture"]) && (options["OutputFormat"] == "FBX" || options["OutputFormat"] == "FBXBinary") && !bool.Parse(options["SaveBlend"]))
							{
								FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\model.fbx", $"{modelDir}\\{modelFile}.fbx");
							}
							else
							{
								blenderNew.Start();
								blenderNew.BeginOutputReadLine();
								blenderNew.BeginErrorReadLine();
								pid_blender = blenderNew.Id;
								blenderNew.WaitForExit();
								if (blenderNew.ExitCode != 0 && token.IsCancellationRequested)
								{
									bw.ReportProgress(0, "Download cancelled.");
									return;
								}

								if (blenderNew.ExitCode != 0)
								{
									if (!bool.Parse(options["SilentMode"]))
									{
										bw.ReportProgress(-1, "MessageBox:Blender crashed or was manually terminated. Skipping conversion...");
									}

									bw.ReportProgress(-1, $"Conversion failed: {Regex.Unescape(modelName)}");

									FileUtils.MoveDir($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\textures", $"{modelDir}\\textures");
									return;
								}

								FileUtils.MoveDir($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\textures", $"{modelDir}\\textures");
								FileUtils.MoveDir($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\vertexColourData", $"{modelDir}\\vertexColourData");
								FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\modelNew{blenderExt}", $"{modelDir}\\{modelFile}{blenderExt}");
								FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\modelNew.quads{blenderExt}", $"{modelDir}\\{modelFile}.quads{blenderExt}");

								if (options["OutputFormat"] == "OBJ")
								{
									FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\modelNew.mtl", $"{modelDir}\\{modelFile}.mtl");
								}

								if (options["OutputFormat"] == "glTF")
								{
									FileUtils.MoveDir($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\textures-gltf", $"{modelDir}\\textures-gltf");
									FileUtils.DeleteFile($"{modelDir}\\{modelFile}.fbx");
								}

								FileUtils.DeleteFile($"{modelDir}\\{modelFile}.blend");
								FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\modelNew.blend", $"{modelDir}\\{modelFile}.blend");
								FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}\\modelNew.quads.blend", $"{modelDir}\\{modelFile}.quads.blend");
							}

							FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\tools\\blender-292\\_sfTemp\\{session}");
						}

						bw.ReportProgress((int)Math.Round(progress = 95));

						if (File.Exists($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\model.blend") && options["OutputFormat"] != "None" && bool.Parse(options["SaveBlend"]) && !bool.Parse(options["AutoTexture"]))
						{
							FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}\\modelNew.blend", $"{modelDir}\\{modelFile}.blend");
						}

						FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\tools\\blender\\_sfTemp\\{session}");

						if (runTwice)
						{
							if (isFirstRun)
							{
								FileUtils.MoveFile($"{modelDir}\\{modelFile}{blenderModelExt}", $"{modelDir}\\{modelFile}_originalPose{blenderModelExt}");
								isFirstRun = false;
								return;
							}
							FileUtils.MoveFile($"{modelDir}\\{modelFile}{blenderModelExt}", $"{modelDir}\\{modelFile}_startPose{blenderModelExt}");
						}
					}

					bw.ReportProgress(-1, "Deleting unnecessary files...");
					switch (options["FilesToDelete"])
					{
						case "Nothing":
							break;
						case "Extras":
							FileUtils.DeleteDir($"{modelDir}\\background");
							FileUtils.DeleteDir($"{modelDir}\\environment");
							FileUtils.DeleteDir($"{modelDir}\\sounds");
							break;
						case "Generated":
							FileUtils.DeleteFile($"{modelDir}\\materialInfo.txt");
							FileUtils.DeleteFile($"{modelDir}\\url.url");
							FileUtils.DeleteDir($"{modelDir}\\animations_action");
							FileUtils.DeleteDir($"{modelDir}\\vertexColourData");
							break;
						case "Non-essential":
							FileUtils.DeleteFile($"{modelDir}\\file.osgjs");
							FileUtils.DeleteFile($"{modelDir}\\file.binz");
							FileUtils.DeleteFile($"{modelDir}\\materialInfo.txt");
							FileUtils.DeleteFile($"{modelDir}\\model_file.bin");
							FileUtils.DeleteFile($"{modelDir}\\model_file_wireframe.bin");
							FileUtils.DeleteFile($"{modelDir}\\model_file.binz");
							FileUtils.DeleteFile($"{modelDir}\\model_file_wireframe.binz");
							FileUtils.DeleteFile($"{modelDir}\\model_wireframe.bin");
							FileUtils.DeleteFile($"{modelDir}\\model_info.json");
							FileUtils.DeleteFile($"{modelDir}\\texture_info.json");
							FileUtils.DeleteFile($"{modelDir}\\url.url");
							FileUtils.DeleteFile($"{modelDir}\\viewer_info.json");
							FileUtils.DeleteDir($"{modelDir}\\animations");
							FileUtils.DeleteDir($"{modelDir}\\animations_action");
							FileUtils.DeleteDir($"{modelDir}\\background");
							FileUtils.DeleteDir($"{modelDir}\\environment");
							FileUtils.DeleteDir($"{modelDir}\\sounds");
							FileUtils.DeleteDir($"{modelDir}\\vertexColourData");
							break;
						case "Everything":
							FileUtils.DeleteFile($"{modelDir}\\file.osgjs");
							FileUtils.DeleteFile($"{modelDir}\\file.binz");
							FileUtils.DeleteFile($"{modelDir}\\materialInfo.txt");
							FileUtils.DeleteFile($"{modelDir}\\model_file.bin");
							FileUtils.DeleteFile($"{modelDir}\\model_file_wireframe.bin");
							FileUtils.DeleteFile($"{modelDir}\\model_file.binz");
							FileUtils.DeleteFile($"{modelDir}\\model_file_wireframe.binz");
							FileUtils.DeleteFile($"{modelDir}\\model_wireframe.bin");
							FileUtils.DeleteFile($"{modelDir}\\model_info.json");
							FileUtils.DeleteFile($"{modelDir}\\texture_info.json");
							FileUtils.DeleteFile($"{modelDir}\\{Path.GetFileName(modelDir)}.thumb.jpeg");
							FileUtils.DeleteFile($"{modelDir}\\url.url");
							FileUtils.DeleteFile($"{modelDir}\\viewer_info.json");
							FileUtils.DeleteDir($"{modelDir}\\animations");
							FileUtils.DeleteDir($"{modelDir}\\animations_action");
							FileUtils.DeleteDir($"{modelDir}\\background");
							FileUtils.DeleteDir($"{modelDir}\\environment");
							FileUtils.DeleteDir($"{modelDir}\\sounds");
							FileUtils.DeleteDir($"{modelDir}\\textures");
							FileUtils.DeleteDir($"{modelDir}\\vertexColourData");
							break;
						default:
							FileUtils.DeleteFile($"{modelDir}\\file.osgjs");
							FileUtils.DeleteFile($"{modelDir}\\file.binz");
							FileUtils.DeleteFile($"{modelDir}\\materialInfo.txt");
							FileUtils.DeleteFile($"{modelDir}\\model_file.bin");
							FileUtils.DeleteFile($"{modelDir}\\model_file_wireframe.bin");
							FileUtils.DeleteFile($"{modelDir}\\model_file.binz");
							FileUtils.DeleteFile($"{modelDir}\\model_file_wireframe.binz");
							FileUtils.DeleteFile($"{modelDir}\\model_wireframe.bin");
							FileUtils.DeleteFile($"{modelDir}\\model_info.json");
							FileUtils.DeleteFile($"{modelDir}\\texture_info.json");
							FileUtils.DeleteFile($"{modelDir}\\url.url");
							FileUtils.DeleteFile($"{modelDir}\\viewer_info.json");
							FileUtils.DeleteDir($"{modelDir}\\animations");
							FileUtils.DeleteDir($"{modelDir}\\animations_action");
							FileUtils.DeleteDir($"{modelDir}\\background");
							FileUtils.DeleteDir($"{modelDir}\\environment");
							FileUtils.DeleteDir($"{modelDir}\\sounds");
							FileUtils.DeleteDir($"{modelDir}\\vertexColourData");
							break;
					}

					if (bool.Parse(options["AutoPackage"]))
					{
						bw.ReportProgress(-1, "Compressing downloaded files...");
						try
						{
							FileUtils.DeleteFile($"{Environment.CurrentDirectory}\\_packaged\\{modelFile}.zip");
							var sevenZip = new Process();
							if (options["UploadTo"] == "Telegram")
							{
								sevenZip.StartInfo.Arguments = $"a -xr!materialInfo.txt -xr!url.url -v2G \"_packaged\\{modelFile}.zip\" \"{modelDir}\\.\"";
							}
							else
							{
								sevenZip.StartInfo.Arguments = $"a -xr!materialInfo.txt -xr!url.url -v500M \"_packaged\\{modelFile}.zip\" \"{modelDir}\\.\"";
							}
							sevenZip.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\7z.exe";
							sevenZip.StartInfo.UseShellExecute = false;
							sevenZip.StartInfo.CreateNoWindow = true;
							sevenZip.Start();
							pid_sevenZip = sevenZip.Id;
							sevenZip.WaitForExit();
							if (sevenZip.ExitCode != 0 && token.IsCancellationRequested)
							{
								bw.ReportProgress(0, "Download cancelled.");
								return;
							}

							FileUtils.CopyFile($"{modelDir}\\{Path.GetFileName(modelDir)}.thumb.jpeg", $"{Environment.CurrentDirectory}\\_packaged\\{modelFile}.jpeg");
							bool isMultiPart = false;
							foreach (var file in Directory.GetFiles($"{Environment.CurrentDirectory}\\_packaged\\"))
							{
								if (Path.GetFileName(file).StartsWith($"{modelPath}.zip"))
								{
									if (Path.GetFileName(file).StartsWith($"{modelPath}.zip.002"))
									{
										isMultiPart = true;
									}
								}
							}

							if (!isMultiPart)
							{
								FileUtils.MoveFile($"{Environment.CurrentDirectory}\\_packaged\\{modelPath}.zip.001", $"{Environment.CurrentDirectory}\\_packaged\\{modelPath}.zip");
							}

							if (options["UploadTo"] == "ZippyShare")
							{
								bw.ReportProgress(-1, "Uploading to ZippyShare...");
								foreach (var file in Directory.GetFiles($"{Environment.CurrentDirectory}\\_packaged\\"))
								{
									if (Path.GetFileName(file).StartsWith($"{modelPath}.zip"))
									{
										FileUtils.MoveFile(file, $"{Environment.CurrentDirectory}\\_packaged\\_zsTemp\\{Path.GetFileName(file)}");
									}
								}

								var zsUpload = new Process();
								zsUpload.StartInfo.Arguments = "-o zippyshareLinks.txt -p _packaged\\_zsTemp\\";
								zsUpload.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\zs-ul_x64.exe";
								zsUpload.StartInfo.UseShellExecute = false;
								zsUpload.StartInfo.CreateNoWindow = true;
								zsUpload.Start();
								pid_zsUpload = zsUpload.Id;
								zsUpload.WaitForExit();
								if (zsUpload.ExitCode != 0 && token.IsCancellationRequested)
								{
									bw.ReportProgress(0, "Download cancelled.");
									return;
								}

								foreach (var file in Directory.GetFiles($"{Environment.CurrentDirectory}\\_packaged\\_zsTemp\\"))
								{
									if (Path.GetFileName(file).StartsWith($"{modelPath}.zip"))
									{
										FileUtils.MoveFile(file, $"{Environment.CurrentDirectory}\\_packaged\\{Path.GetFileName(file)}");
									}
								}

								FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\_packaged\\_zsTemp");
							}
							/*else if (options["UploadTo"] == "Telegram" && bool.Parse(options["TGSetup"]))
							{
								if (tgClient.UserLoginStatus == TelegramEnums.UserLoginStatus.Logined)
								{
									foreach (var file in Directory.GetFiles($"{Environment.CurrentDirectory}\\_packaged\\").Where(filePath => Path.GetFileName(filePath).StartsWith($"{modelPath}.zip")))
									{
										long chatID = long.Parse(options["TGChatID"]);
										var tgChats = await tgClient.GetChats();
										var sendMessage = tgClient.SendLocalFileToChat(chatID, file, "");
										Debugger.Break();
									}
								}
							}*/

							if (bool.Parse(options["SortPackagedByArtist"]) && options["UploadTo"] != "Telegram")
							{
								FileUtils.MoveFile($"{Environment.CurrentDirectory}\\_packaged\\{modelFile}.jpeg", $"{Environment.CurrentDirectory}\\_packaged\\{modelAuthor}\\{modelFile}.jpeg");
								FileUtils.MoveFile($"{Environment.CurrentDirectory}\\_packaged\\{modelFile}.zip", $"{Environment.CurrentDirectory}\\_packaged\\{modelAuthor}\\{modelFile}.zip");
							}
						}
						catch (Exception e)
						{
							if (!bool.Parse(options["SilentMode"]))
							{
								bw.ReportProgress(-1, $"MessageBox:Failed to compress files! Exception:{Environment.NewLine}{e}");
							}
						}
					}

					if (bool.Parse(options["SortByArtist"]))
					{
						FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\downloads\\{modelAuthor}\\{modelPath}");
						FileUtils.MoveDir($"{Environment.CurrentDirectory}\\downloads\\{modelPath}", $"{Environment.CurrentDirectory}\\downloads\\{modelAuthor}\\{modelPath}");
						FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\downloads\\{modelPath}");
					}

					retryNum = 0;
					bw.ReportProgress((int)Math.Round(progress = 100));
				}
				catch (FileNotFoundException e)
				{
					if (!bool.Parse(options["SilentMode"]))
					{
						if (e.ToString().Contains("file.osgjs"))
						{
							bw.ReportProgress(-1, $"MessageBox:\"file.osgjs\" could not be found, .binz decryption has likely failed. {Environment.NewLine}{Environment.NewLine}Exception:{Environment.NewLine}{e}{Environment.NewLine}{Environment.NewLine}Press OK to continue.");
						}
						else
						{
							bw.ReportProgress(-1,
								$"MessageBox:A file was not found during conversion, .binz decryption has likely failed. Other causes for this issue include Blender crashing, placing the tool in a path with special characters, or a network issue preventing files from being downloaded.{Environment.NewLine}{Environment.NewLine}Exception:{Environment.NewLine}{e}{Environment.NewLine}{Environment.NewLine}Press OK to continue.");
						}
					}
				}
				catch (Exception e)
				{
					isIndexFix = false;

					if (retryNum < 3 && !bool.Parse(options["SilentMode"]))
					{
						if (retryAll)
						{
							retryNum++;
							return;
						}
						bw.ReportProgress(-1, $"MessageBoxYesNo:{e}{Environment.NewLine}Retry download?");
						if (messageBoxResult == "MessageBoxResult_Yes")
						{
							retryNum++;
							if (urlType == "Artist" || isBatch)
							{
								if (!hasAskedToRetry)
								{
									hasAskedToRetry = true;
									bw.ReportProgress(-1, "MessageBoxYesNo:Retry all future downloads without asking?");
									if (messageBoxResult == "MessageBoxResult_Yes")
									{
										retryAll = true;
									}
								}
							}
							return;
						}
					}
					isIndexFix = false;
					retryNum = 0;
					return;
				}
				Thread.Sleep(100);
				bw.ReportProgress(-1, $"Download complete: {Regex.Unescape(modelName)}");
			}
			catch (Exception e)
			{
				Logger.Log("Error:", Logger.ErrorLevel.Error, exception: e.ToString());
			}
		}

	}
}
