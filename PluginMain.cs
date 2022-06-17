using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json.Linq;
using PluginInterface;
using SketchfabRipper;
//using TelegramLib;

namespace SketchfabPlugin
{
	[Export(typeof(IPlugin))]
	public class PluginMain : IPlugin
	{
		public string[] domains => new string[] { "sketchfab.com", "www.sketchfab.com", "massive.sketchfab.com", "skfb.ly", "web.archive.org", "3dripper.com" };

		public string[] urlPatterns => new string[] {};

		public string data
		{
			get => data;
			set => SketchfabDL.messageBoxResult = value;
		}

		public Dictionary<string, string> ParseURL(string url)
		{
			var webClient = new WebClient();
			if (url.Contains("skfb.ly/"))
			{
				HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
				HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
				url = resp.ResponseUri.ToString();
			}
			url = Regex.Match(url, "([^?]+).*").Groups[1].Value;
			if (url.Contains("/models/"))
			{
				url = url.Replace("/models/", "/3d-models/");
				if (url.EndsWith("/embed"))
				{
					url = url.Replace("/embed", "");
				}
			}
			var metadataDict = new Dictionary<string, string>();
			if (url.Contains("massive.sketchfab.com"))
			{
				return metadataDict;
			}
			if (url.StartsWith("https://sketchfab.com/3d-models/"))
			{
				var modelID = Regex.Match(url, "[^-]+$", RegexOptions.RightToLeft).Value;
				if (!Regex.Match(url, "models/.*-(.*)").Success)
				{
					modelID = Regex.Match(url, "(?<=models/).*").Value.Trim('-');
				}
				string jsonData;
				try
				{
					jsonData = webClient.DownloadString($"https://sketchfab.com/i/models/{modelID}");
				}
				catch (WebException)
				{
					return metadataDict;
				}
				JObject jObject = JObject.Parse(jsonData);
				string thumbURL = null;
				int maxValue = 0;
				foreach (var img in jObject["thumbnails"]["images"])
				{
					int num = int.Parse(img["size"].ToString());
					if (num > maxValue)
					{
						maxValue = num;
						thumbURL = img["url"].ToString();
					}
				}
				metadataDict.Add("Header", jObject["name"].ToString());
				metadataDict.Add("Metadata1", $"Vertices: {jObject["vertexCount"]}");
				metadataDict.Add("Metadata2", $"Polygons: {jObject["faceCount"]}");
				metadataDict.Add("Metadata3", $"Animations: {jObject["animationCount"]}");
				metadataDict.Add("Thumbnail", thumbURL.Replace("\\", ""));
			}
			else if (url.StartsWith("https://sketchfab.com/") && url.Contains("/collections/"))
			{
				string collectionID = Regex.Match(webClient.DownloadString(url), "(?<=/i/collections/).*?(?=/models)").ToString();
				JObject collectionInfo = JObject.Parse(webClient.DownloadString($"https://sketchfab.com/i/collections/{collectionID}"));
				int maxValue = 0;
				string avatarURL = null;
				foreach (var j in collectionInfo["user"]["avatars"]["images"])
				{
					int num = int.Parse(j["size"].ToString());
					if (num > maxValue)
					{
						maxValue = num;
						avatarURL = j["url"].ToString();
					}
				}
				metadataDict.Add("Header", collectionInfo["name"].ToString());
				metadataDict.Add("Metadata1", $"Models: {collectionInfo["modelCount"]}");
				metadataDict.Add("Thumbnail", avatarURL.Replace("\\", ""));
			}
			else if (url.StartsWith("https://sketchfab.com/") && url.EndsWith("/likes"))
			{
				string userID = Regex.Match(webClient.DownloadString(url), "(?<=data-profile-user=\").*?(?=\")").ToString();
				JObject userInfo = JObject.Parse(webClient.DownloadString($"https://sketchfab.com/i/users/{userID}"));
				int maxValue = 0;
				string avatarURL = null;
				foreach (var img in userInfo["avatars"]["images"])
				{
					int num = int.Parse(img["size"].ToString());
					if (num > maxValue)
					{
						maxValue = num;
						avatarURL = img["url"].ToString();
					}
				}
				metadataDict.Add("Header", userInfo["displayName"].ToString());
				metadataDict.Add("Metadata1", $"Liked models: {userInfo["likeCount"]}");
				metadataDict.Add("Thumbnail", avatarURL.Replace("\\", ""));
			}
			else if (url.StartsWith("https://sketchfab.com/"))
			{
				var userName = Regex.Match(url, "(?<=.com/)([^/]+)").Value;
				string uid = null;
				try
				{
					string userApiData = webClient.DownloadString($"https://api.sketchfab.com/v3/search?type=users&username={userName}");
					uid = JObject.Parse(userApiData)["results"][0]["uid"].ToString();
					if (JObject.Parse(userApiData)["results"][0]["username"].ToString() != userName)
					{
						throw new Exception();
					}
				}
				catch
				{
					try
					{
						string userProfile = webClient.DownloadString($"https://sketchfab.com/{userName}");
						var uidRegex = Regex.Match(userProfile, "(?<=data-profile-user=\").*?(?=\")").Value;
						uid = uidRegex;
					}
					catch {}
				}
				if (string.IsNullOrEmpty(uid))
				{
					return metadataDict;
				}
				string userData = webClient.DownloadString($"https://sketchfab.com/i/users/{uid}");
				int maxValue = 0;
				string avatarURL = null;
				JObject jObject = JObject.Parse(userData);
				foreach (var j in jObject["avatars"]["images"])
				{
					int num = int.Parse(j["size"].ToString());
					if (num > maxValue)
					{
						maxValue = num;
						avatarURL = j["url"].ToString();
					}
				}
				metadataDict.Add("Header", jObject["displayName"].ToString());
				metadataDict.Add("Metadata1", $"Models: {jObject["modelCount"]}");
				metadataDict.Add("Thumbnail", avatarURL.Replace("\\", ""));
			}
			return metadataDict;
		}

		public string Process(string url, Dictionary<string, string> options, BackgroundWorker bw, CancellationToken token)
		{
			try
			{
				var webClient = new WebClient();
				if (url.Contains("skfb.ly/"))
				{
					HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
					HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
					url = resp.ResponseUri.ToString();
				}
				url = Regex.Match(url, "([^?]+).*").Groups[1].Value;
				/*if (url.Contains("massive.sketchfab.com"))
				{
					MassiveDL.Download(url, bw);
				}*/
				Logger.Log($"Downloading Sketchfab model from {url}", Logger.ErrorLevel.Info);
				if (options["SFAuthType"] == "SessionID" && !string.IsNullOrEmpty(options["SFSessionID"]))
				{
					try
					{
						bw.ReportProgress(-1, "Checking session ID...");
						CookieContainer cookieContainer = new CookieContainer();
						cookieContainer.Add(new Cookie("sb_sessionid", options["SFSessionID"], "/", "sketchfab.com"));
						HttpWebRequest httpWebRequest = WebRequest.CreateHttp("https://sketchfab.com/i/archives/latest?archive_type=source&model=96340701c2ed4d37851c7d9109eee9c0");
						httpWebRequest.Method = "GET";
						httpWebRequest.CookieContainer = cookieContainer;
						httpWebRequest.Timeout = 4000;
						var responseStream = httpWebRequest.GetResponse().GetResponseStream();
						StreamReader readStream = new StreamReader(responseStream, Encoding.UTF8);
						if (readStream.ReadToEnd().Contains("Authentication credentials were not provided."))
						{
							throw new WebException();
						}
					}
					catch (WebException)
					{
						if (!string.IsNullOrEmpty(options["SFAccessToken"]))
						{
							if (!bool.Parse(options["SilentMode"]))
							{
								bw.ReportProgress(-1, "MessageBox:Session ID has likely expired. Switching to access token...");
							}
							options["SFAuthType"] = "AccessToken";
							options["SFSessionID"] = null;
						}
						else
						{
							if (!bool.Parse(options["SilentMode"]))
							{
								bw.ReportProgress(-1, "MessageBox:Session ID has likely expired. Please re-authenticate to continue downloading original model data.");
							}
							options["SFAuthType"] = null;
							options["SFSessionID"] = null;
						}
					}
					catch {}
				}
				if (int.Parse(options["SFLastAuth"]) < DateTimeOffset.Now.ToUnixTimeSeconds() - 2500000 && options["SFAuthType"] == "AccessToken")
				{
					bw.ReportProgress(-1, "Refreshing Sketchfab access token...");
					HttpWebRequest httpWebRequest = WebRequest.CreateHttp("https://sketchfab.com/oauth2/token/");
					httpWebRequest.Method = "POST";
					httpWebRequest.ContentType = "application/x-www-form-urlencoded";
					httpWebRequest.Timeout = 4000;
					httpWebRequest.Proxy = null;
					byte[] bytes = Encoding.UTF8.GetBytes($"grant_type=refresh_token&client_id=hGC7unF4BHyEB0s7Orz5E1mBd3LluEG0ILBiZvF9&refresh_token={options["SFRefreshToken"]}");
					try
					{
						using (Stream requestStream = httpWebRequest.GetRequestStream())
						{
							requestStream.Write(bytes, 0, bytes.Length);
						}

						var responseStream = httpWebRequest.GetResponse().GetResponseStream();
						StreamReader readStream = new StreamReader(responseStream, Encoding.UTF8);

						try
						{
							var responseJSON = JObject.Parse(readStream.ReadToEnd());
							options["SFAccessToken"] = responseJSON["access_token"].ToString();
							options["SFLastAuth"] = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
							options["SFRefreshToken"] = responseJSON["refresh_token"].ToString();
						}
						catch
						{
							if (!bool.Parse(options["SilentMode"]))
							{
								bw.ReportProgress(-1, "MessageBox:Error parsing response! Invalid refresh token? Please re-authenticate to continue downloading original model data.");
							}
							options["SFAccessToken"] = null;
							options["SFAuthType"] = null;
							options["SFLastAuth"] = "0";
							options["SFRefreshToken"] = null;
						}
					}
					catch {}
				}

				if (url.Contains("/models/"))
				{
					url = url.Replace("/models/", "/3d-models/");
					if (url.EndsWith("/embed"))
					{
						url = url.Replace("/embed", "");
					}
				}

				string[] modelURLs = {};
				if (url.StartsWith("https://sketchfab.com/3d-models/"))
				{
					Array.Resize(ref modelURLs, modelURLs.Length + 1);
					modelURLs[modelURLs.GetUpperBound(0)] = url;
					SketchfabDL.urlType = "Model";
				}
				else if (url.StartsWith("https://sketchfab.com/") && url.Contains("/collections/"))
				{
					string collectionID = Regex.Match(webClient.DownloadString(url), "(?<=/i/collections/)(.*?)(?=/models)").ToString();
					JObject collectionInfo = JObject.Parse(webClient.DownloadString($"https://sketchfab.com/i/collections/{collectionID}/models?restricted=1&sort_by=-collectedAt"));
					for (int i = 1;; i++)
					{
						bw.ReportProgress(-1, $"Building model list (Page: {i})...");
						foreach (var result in collectionInfo["results"])
						{
							if (!modelURLs.Contains(result["viewerUrl"].ToString()))
							{
								Array.Resize(ref modelURLs, modelURLs.Length + 1);
								modelURLs[modelURLs.GetUpperBound(0)] = result["viewerUrl"].ToString();
							}
						}
						if (string.IsNullOrEmpty(collectionInfo["next"].ToString()))
						{
							break;
						}
						collectionInfo = JObject.Parse(webClient.DownloadString(collectionInfo["next"].ToString()));
					}
					SketchfabDL.urlType = "Collection";
				}
				else if (url.StartsWith("https://sketchfab.com/") && url.EndsWith("/likes"))
				{
					string userID = Regex.Match(webClient.DownloadString(url), "(?<=data-profile-user=\")(.*?)(?=\")").ToString();
					JObject likedModels = JObject.Parse(webClient.DownloadString($"https://sketchfab.com/i/likes?restricted=1&sort_by=-createdAt&liked_by={userID}"));
					for (int i = 1;; i++)
					{
						bw.ReportProgress(-1, $"Building model list (Page: {i})...");
						foreach (var result in likedModels["results"])
						{
							if (!modelURLs.Contains(result["viewerUrl"].ToString()))
							{
								Array.Resize(ref modelURLs, modelURLs.Length + 1);
								modelURLs[modelURLs.GetUpperBound(0)] = result["viewerUrl"].ToString();
							}
						}
						if (string.IsNullOrEmpty(likedModels["next"].ToString()))
						{
							break;
						}
						likedModels = JObject.Parse(webClient.DownloadString(likedModels["next"].ToString()));
					}
					SketchfabDL.urlType = "Collection";
				}
				else if (url.StartsWith("https://sketchfab.com/"))
				{
					string userName = Regex.Match(url, "(?<=\\.com/).*").Value;
					string uid = null;
					try
					{
						string userApiData = webClient.DownloadString($"https://api.sketchfab.com/v3/search?type=users&username={userName}");
						uid = JObject.Parse(userApiData)["results"][0]["uid"].ToString();
						if (JObject.Parse(userApiData)["results"][0]["username"].ToString() != userName)
						{
							throw new Exception();
						}
					}
					catch
					{
						try
						{
							string userProfile = webClient.DownloadString($"https://sketchfab.com/{userName}");
							uid = Regex.Match(userProfile, "(?<=data-profile-user=\").*?(?=\")").Value;
						}
						catch {}
					}
					if (string.IsNullOrEmpty(uid))
					{
						return "Failed to extract user ID!";
					}
					string jsonDataURL = $"https://sketchfab.com/i/models?restricted=1&sort_by=-publishedAt&user={uid}";
					for (int i = 1;; i++)
					{
						try
						{
							bw.ReportProgress(-1, $"Building model list (Page: {i})...");
							string jsonData = webClient.DownloadString(jsonDataURL);
							foreach (var model in JObject.Parse(jsonData)["results"])
							{
								Array.Resize(ref modelURLs, modelURLs.Length + 1);
								modelURLs[modelURLs.GetUpperBound(0)] = model["viewerUrl"].ToString().Replace("\\", "");
							}
							jsonDataURL = JObject.Parse(jsonData)["next"].ToString().Replace("\\", "");
						}
						catch
						{
							break;
						}
					}
					SketchfabDL.urlType = "Artist";
				}

				foreach (var modelURL in modelURLs)
				{
					SketchfabDL.isFirstRun = true;
					if (!string.IsNullOrEmpty(options["SFConfig"]))
					{
						SketchfabDL.hasConfigData = true;
					}
					try
					{
						if (token.IsCancellationRequested)
						{
							return null;
						}
						if (bool.Parse(options["isBatch"]))
						{
							SketchfabDL.isBatch = true;
						}
						SketchfabDL.needsToRetry = false;
						SketchfabDL.isIndexFix = false;
						if (int.Parse(options["SendToStartPose"]) == 2)
						{
							SketchfabDL.Download(modelURL, options, bw, token, true);
							SketchfabDL.Download(modelURL, options, bw, token, true);
						}
						else
						{
							SketchfabDL.Download(modelURL, options, bw, token);
							if (SketchfabDL.needsToRetry && !token.IsCancellationRequested)
							{
								SketchfabDL.isIndexFix = true;
								SketchfabDL.Download(modelURL, options, bw, token);
							}

							while (SketchfabDL.retryNum > 0 && !token.IsCancellationRequested)
							{
								SketchfabDL.needsToRetry = false;
								SketchfabDL.isIndexFix = false;
								SketchfabDL.Download(modelURL, options, bw, token);
								if (SketchfabDL.needsToRetry && !token.IsCancellationRequested)
								{
									SketchfabDL.isIndexFix = true;
									SketchfabDL.Download(modelURL, options, bw, token);
								}
							}
						}
					}
					catch
					{
					}
				}
			}
			catch (Exception e)
			{
				return e.ToString();
			}
			if (SketchfabDL.newConfSet)
			{
				return $"SFNewConf|{SketchfabDL.newConf}";
			}
			return null;
		}
	}
}