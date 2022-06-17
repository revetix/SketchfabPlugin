using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SketchfabRipper;

namespace SketchfabPlugin
{
	public class DumpResources
	{
		public static bool isEncrypted;

		public static JObject viewerData;

		private static bool colourDone;
		private static bool uniqueMode;

		private static string textureMode;

		private static string[] downloadedUIDs = {};
		private static string[] matUIDs = {};
		private static string[] matTextures = {};

		private static void DecompressGZip(string filePath)
		{
			try
			{
				byte[] gZipData = File.ReadAllBytes(filePath);
				Stream gZipStream = new MemoryStream(gZipData);
				FileStream gZipDecompressedFile = File.Create(filePath.Substring(0, filePath.Length - 3));
				GZipStream gZipDecompressStream = new GZipStream(gZipStream, CompressionMode.Decompress);
				gZipDecompressStream.CopyTo(gZipDecompressedFile);
				gZipDecompressedFile.Close();
			}
			catch {}
		}

		private static string BeautifyJSON(string json)
		{
			var jObj = JsonConvert.DeserializeObject(json);
			return JsonConvert.SerializeObject(jObj, Formatting.Indented);
		}

		private static string GetLargestTexture(string uid, CancellationToken token)
		{
			var webClient = new WebClient();
			string json = webClient.DownloadString($"https://sketchfab.com/i/textures/{uid}");
			JObject jObject = JObject.Parse(json);

			int[] textureSizes = {};
			int[] textureResolutions = {};
			string[] textureURLs = {};
			string[] textureDimensions = {};
			for (int i = 0;; i++)
			{
				if (token.IsCancellationRequested)
				{
					return null;
				}
				try
				{
					int x = int.Parse(jObject["images"][i]["width"].ToString());
					int y = int.Parse(jObject["images"][i]["height"].ToString());
					int texturePixelCount = x * y;
					string textureSize = jObject["images"][i]["size"].ToString();
					string textureURL = jObject["images"][i]["url"].ToString();
					if (string.IsNullOrEmpty(textureSize))
					{
						var httpClient = new HttpClient();
						var request = new HttpRequestMessage(HttpMethod.Get, textureURL);
						var response = httpClient.SendAsync(request, token).Result;
						textureSize = response.Content.Headers.GetValues("Content-Length").FirstOrDefault();
					}
					Array.Resize(ref textureResolutions, textureResolutions.Length + 1);
					textureResolutions[textureResolutions.GetUpperBound(0)] = texturePixelCount;
					Array.Resize(ref textureSizes, textureSizes.Length + 1);
					textureSizes[textureSizes.GetUpperBound(0)] = int.Parse(textureSize);
					Array.Resize(ref textureURLs, textureURLs.Length + 1);
					textureURLs[textureURLs.GetUpperBound(0)] = textureURL;
					Array.Resize(ref textureDimensions, textureDimensions.Length + 1);
					textureDimensions[textureDimensions.GetUpperBound(0)] = $"{x}x{y}";
				}
				catch
				{
					break;
				}
			}

			int maxValue = textureResolutions.Max();
			var maxIndices = textureResolutions.Select((x, i) => new {Index = i, Value = x}).Where(x => x.Value == maxValue).Select(x => x.Index);
			int maxSize = 0;
			foreach (var index in maxIndices)
			{
				if (textureSizes[index] > maxSize)
				{
					maxSize = textureSizes[index];
				}
			}
			int maxIndex = textureSizes.ToList().IndexOf(maxSize);
			return $"{textureDimensions[maxIndex]}|{textureURLs[maxIndex]}";
		}

		private static void GetSounds(string uid, string modelPath, BackgroundWorker bw, CancellationToken token)
		{
			try
			{
				var webClient = new WebClient();
				string json = webClient.DownloadString($"https://sketchfab.com/i/models/{uid}/sounds");
				JObject jObject = JObject.Parse(json);

				for (int i = 0;; i++)
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					try
					{
						string soundName = jObject["results"][i]["name"].ToString();
						string soundUrl = jObject["results"][i]["files"][0]["url"].ToString();
						Directory.CreateDirectory($"{modelPath}\\sounds\\");
						bw.ReportProgress(-1, $"Downloading \"{soundName}.mp3\"");
						webClient.DownloadFile(soundUrl, $"{modelPath}\\sounds\\{soundName}.mp3");
					}
					catch
					{
						break;
					}
				}
			}
			catch {}
		}

		private static void GetBackground(string uid, string modelPath, BackgroundWorker bw, CancellationToken token)
		{
			try
			{
				var webClient = new WebClient();
				string json = webClient.DownloadString($"https://sketchfab.com/i/backgrounds/{uid}");
				JObject jObject = JObject.Parse(json);

				int[] bgSizes = {};
				string[] bgURLs = {};
				for (int i = 0;; i++)
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					try
					{
						string bgSize = jObject["images"][i]["size"].ToString();
						string bgURL = jObject["images"][i]["url"].ToString();
						if (string.IsNullOrEmpty(bgSize))
						{
							webClient.OpenRead(bgURL);
							bgSize = webClient.ResponseHeaders["Content-Length"];
						}
						Array.Resize(ref bgSizes, bgSizes.Length + 1);
						bgSizes[bgSizes.GetUpperBound(0)] = int.Parse(bgSize);
						Array.Resize(ref bgURLs, bgURLs.Length + 1);
						bgURLs[bgURLs.GetUpperBound(0)] = bgURL;
					}
					catch
					{
						break;
					}
				}
				int maxIndex = bgSizes.ToList().IndexOf(bgSizes.Max());
				Directory.CreateDirectory($"{modelPath}\\background");
				string fileName = jObject["name"].ToString();
				if (!fileName.EndsWith(".jpg") && !fileName.EndsWith(".jpeg") && !fileName.EndsWith(".png") && !fileName.EndsWith(".tga") && !fileName.EndsWith(".tif"))
				{
					fileName += Path.GetExtension(bgURLs[maxIndex]);
				}
				bw.ReportProgress(-1, $"Downloading \"{fileName}\"");
				webClient.DownloadFile(bgURLs[maxIndex], $"{modelPath}\\background\\{fileName}");
			}
			catch {}
		}

		private static void GetEnvironment(string uid, string modelPath, BackgroundWorker bw, CancellationToken token)
		{
			try
			{
				var webClient = new WebClient();
				string envJSON = webClient.DownloadString($"https://sketchfab.com/i/environments/{uid}");
				JObject jObject = JObject.Parse(envJSON);

				bool complete = false;
				for (int i = 0;; i++)
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					for (int j = 0;; j++)
					{
						try
						{
							string envURL = jObject["textures"][i]["images"][j]["file"].ToString();
							string envName = jObject["textures"][i]["images"][j]["name"].ToString();
							Directory.CreateDirectory($"{modelPath}\\environment");
							bw.ReportProgress(-1, $"Downloading \"{envName}\"");
							webClient.DownloadFile(envURL, $"{modelPath}\\environment\\{envName}");
						}
						catch
						{
							if (j == 0)
							{
								complete = true;
							}
							break;
						}
					}
					if (complete)
					{
						break;
					}
				}
				File.WriteAllText($"{modelPath}\\environment\\environment_info.json", BeautifyJSON(envJSON));
			}
			catch {}

			foreach (string file in Directory.GetFiles($"{modelPath}\\environment\\").Where(file => file.EndsWith(".gz")))
			{
				try
				{
					bw.ReportProgress(-1, ($"Decompressing \"{Path.GetFileName(file)}\""));
					DecompressGZip(file);
					File.Delete(file);
				}
				catch {}
			}

			foreach (string file in Directory.GetFiles($"{modelPath}\\environment\\").Where(file => file.EndsWith(".bin")))
			{
				try
				{
					File.Move(file, $"{file}.gz");
				}
				catch {}
			}
		}

		private static void GetARData(string uid, string modelPath, BackgroundWorker bw, CancellationToken token)
		{
			try
			{
				var webClient = new WebClient();
				string modelJSON = webClient.DownloadString($"https://sketchfab.com/i/models/{uid}");
				JObject jObject = JObject.Parse(modelJSON);
				if (bool.Parse(jObject["isArAvailable"].ToString()))
				{
					string androidURL = JObject.Parse(webClient.DownloadString($"https://sketchfab.com/i/archives/ar?model={uid}&platform=android"))["url"].ToString();
					string iosURL = JObject.Parse(webClient.DownloadString($"https://sketchfab.com/i/archives/ar?model={uid}&platform=ios"))["url"].ToString();
					string androidName = Path.GetFileName(androidURL);
					string iosName = Path.GetFileName(iosURL.Substring(0, iosURL.IndexOf("?")));
					Directory.CreateDirectory($"{modelPath}\\ar\\android");
					Directory.CreateDirectory($"{modelPath}\\ar\\ios");
					bw.ReportProgress(-1, $"Downloading \"{androidName}\"");
					webClient.DownloadFile(androidURL, $"{modelPath}\\ar\\android\\{androidName}");
					bw.ReportProgress(-1, $"Downloading \"{iosName}\"");
					webClient.DownloadFile(iosURL, $"{modelPath}\\ar\\ios\\{iosName}");

					JObject gltfData = JObject.Parse(File.ReadAllText($"{modelPath}\\ar\\android\\{androidName}"));
					bw.ReportProgress(-1, $"Downloading \"{Path.GetFileNameWithoutExtension(androidName)}.bin\"");
					webClient.DownloadFile(androidURL.Replace(".gltf", ".bin"), $"{modelPath}\\ar\\android\\{Path.GetFileNameWithoutExtension(androidName)}.bin");
					foreach (var texture in gltfData["images"])
					{
						if (token.IsCancellationRequested)
						{
							return;
						}
						bw.ReportProgress(-1, $"Downloading \"{Path.GetFileName(texture["uri"].ToString())}\"");
						Directory.CreateDirectory($"{modelPath}\\ar\\android\\textures");
						webClient.DownloadFile(androidURL.Replace(androidName, texture["uri"].ToString()), $"{modelPath}\\ar\\android\\textures\\{Path.GetFileName(texture["uri"].ToString())}");
					}
				}
			}
			catch {}
		}

		private static async void GetTextureName(JObject jObject, string id, string modelPath, string prefix, string type, BackgroundWorker bw, CancellationToken token)
		{
			string texture = null;
			try
			{
				try
				{
					if (!bool.Parse(jObject["options"]["materials"][id]["channels"][type]["enable"].ToString()))
					{
						return;
					}
				}
				catch {}

				try
				{
					if (type == "NormalMap" || type == "ClearCoatNormalMap")
					{
						bool isFlipped = bool.Parse(jObject["options"]["materials"][id]["channels"][type]["flipY"].ToString());
						if (isFlipped)
						{
							prefix = $"{prefix.Replace(": ", "")} (Flipped Y): ";
						}
					}
					if (type == "Opacity")
					{
						string alphaType = jObject["options"]["materials"][id]["channels"][type]["type"].ToString();
						prefix = $"{prefix.Replace(": ", "")} ({alphaType}): ";
						try
						{
							if (bool.Parse(jObject["options"]["materials"][id]["channels"][type]["invert"].ToString()))
							{
								prefix = $"{prefix.Replace(": ", "")} (Inverted): ";
							}
						}
						catch {}
						if (jObject["options"]["materials"][id]["channels"][type]["factor"].ToString() == "1")
						{
							try
							{
								string test = jObject["options"]["materials"][id]["channels"][type]["texture"]["uid"].ToString();
							}
							catch
							{
								return;
							}
						}
						string alphaChannel = jObject["options"]["materials"][id]["channels"][type]["texture"]["internalFormat"].ToString();
						prefix = $"{prefix.Replace(": ", "")} ({alphaChannel}): ";
					}
				}
				catch {}

				try
				{
					string texCoord = jObject["options"]["materials"][id]["channels"][type]["texture"]["texCoordUnit"].ToString();
					prefix = $"{prefix.Replace(": ", "")} (UV{texCoord}): ";
				}
				catch {}

				texture = jObject["options"]["materials"][id]["channels"][type]["texture"]["uid"].ToString();
				int idIndex = matUIDs.ToList().IndexOf(texture);
				texture = matTextures[idIndex];
				try
				{
					string fac = jObject["options"]["materials"][id]["channels"][type]["factor"].ToString();
					if (fac != "1")
					{
						prefix = $"{prefix.Replace(": ", "")} (Factor={fac}): ";
					}
				}
				catch {}
			}
			catch (IndexOutOfRangeException)
			{
				try
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					var webClient = new WebClient();
					bw.ReportProgress(-1, $"Missing texture \"{texture}\". Downloading now...");
					var textureInfo = await Task.Run(() => GetLargestTexture(texture, token).Split('|'));
					string textureURL = textureInfo[1];
					int[] textureRes = {int.Parse(textureInfo[0].Split('x')[0]), int.Parse(textureInfo[0].Split('x')[1])};
					if (string.IsNullOrEmpty(textureURL) && token.IsCancellationRequested)
					{
						return;
					}
					var tmpObj = JObject.Parse(webClient.DownloadString($"https://sketchfab.com/i/textures/{texture}"));
					string textureName = tmpObj["name"].ToString();
					string fileName = Path.GetFileNameWithoutExtension(textureName);
					string fileExtension = Path.GetExtension(textureName);
					if (textureMode == "AppendID")
					{
						fileName = $"{Path.GetFileNameWithoutExtension(textureName)}.{texture}";
					}
					else if (textureMode == "AppendNum")
					{
						int i = 2;
						while (File.Exists($"{modelPath}\\textures\\{fileName}{fileExtension}"))
						{
							fileName = $"{Path.GetFileNameWithoutExtension(textureName)}.{i}";
							i++;
						}
					}
					webClient.DownloadFile(textureURL, $"{modelPath}\\textures\\{fileName}{fileExtension}");
					var texturePK = tmpObj["images"][0]["pk"].ToString();
					var texturePV = tmpObj["images"][0]["pv"].ToString();
					if (texturePK != "null" && !SketchfabDL.isPublicBuild)
					{
						// REMOVED: Texture unscrambling
					}
					texture = fileName + fileExtension;
				}
				catch {}
			}
			catch
			{
				try
				{
					texture = jObject["options"]["materials"][id]["channels"][type]["color"].ToString().Replace("\n", "").Replace("\r", "").Replace("[", "").Replace("]", "").Replace(",", "|").Replace(" ", "");
					if (type == "AlbedoPBR" || type == "DiffusePBR" || type == "DiffuseColor")
					{
						if (texture == "1|1|1")
						{
							if (type != "DiffuseColor" && !colourDone)
							{
								return;
							}
						}
						else
						{
							colourDone = true;
						}
					}

					try
					{
						if (type == "EmitColor" || type == "AlbedoPBR" || type == "DiffusePBR" || type == "DiffuseColor")
						{
							string texNew = null;
							float factor = jObject["options"]["materials"][id]["channels"][type]["factor"].Value<float>();
							string[] colourValues = texture.Split('|');
							foreach (var colour in colourValues)
							{
								texNew = $"{texNew}{float.Parse(colour) * factor}|";
							}
							texNew = texNew.TrimEnd('|');
							texture = texNew;
						}
						if (type == "MetalnessPBR" || type == "Displacement" || type == "GlossinessPBR" || type == "RoughnessPBR" || type == "SpecularF0" || type == "Opacity")
						{
							float factor = jObject["options"]["materials"][id]["channels"][type]["factor"].Value<float>();
							texture = texture.Remove(texture.IndexOf('|'));
							texture = (float.Parse(texture) * factor).ToString();
						}
					}
					catch {}
				}
				catch
				{
					try
					{
						texture = jObject["options"]["materials"][id]["channels"][type]["factor"].ToString();
					}
					catch {}
				}
			}

			if (string.IsNullOrEmpty(texture))
			{
				return;
			}

			if (texture.Contains(".jpg") || texture.Contains(".jpeg") || texture.Contains(".tga") || texture.Contains(".tif") || texture.Contains(".dds"))
			{
				texture = texture.Remove(texture.LastIndexOf(".")) + ".png";
			}

			File.AppendAllText($"{modelPath}\\materialInfo.txt", prefix + texture + Environment.NewLine);
		}

		private static void MaterialParser(string modelPath, BackgroundWorker bw, CancellationToken token)
		{
			try
			{
				try
				{
					File.Delete($"{modelPath}\\materialInfo.txt");
				}
				catch {}
				bw.ReportProgress(-1, "Parsing materials...");
				JObject modelInfo = viewerData;
				JObject osgjs = JObject.Parse(File.ReadAllText($"{modelPath}\\file.osgjs"));
				var list = osgjs.DescendantsAndSelf();

				string[] materialIDs = {};
				string[] materialUIDs = {};
				string[] materialNames = {};
				string[] materialIDs2 = {};
				string[] materialSSIDs2 = {};
				string[] materialNames2 = {};
				string[] meshNames = {};
				string ver = Assembly.GetExecutingAssembly().GetName().Version.ToString();
				if (ver.EndsWith(".0"))
				{
					ver = ver.Substring(0, ver.Length - 2);
				}
				File.AppendAllText($"{modelPath}\\materialInfo.txt", $"// Generated using SketchfabRipper v{ver} by revetix#9971{Environment.NewLine}");
				foreach (var jToken in modelInfo["options"]["materials"])
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					foreach (var jtoken2 in jToken)
					{
						try
						{
							JObject matObj = jtoken2.ToObject<JObject>();
							string matID = matObj["id"].ToString();
							string matName = matObj["name"].ToString();
							string matSSID = matObj["stateSetID"].ToString();
							Array.Resize(ref materialIDs2, materialIDs2.Length + 1);
							materialIDs2[materialIDs2.GetUpperBound(0)] = matID;
							Array.Resize(ref materialNames2, materialNames2.Length + 1);
							materialNames2[materialNames2.GetUpperBound(0)] = matName;
							Array.Resize(ref materialSSIDs2, materialSSIDs2.Length + 1);
							materialSSIDs2[materialSSIDs2.GetUpperBound(0)] = matSSID;
						}
						catch {}
					}
				}
				foreach (var jToken in list)
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					try
					{
						if (jToken.Path.EndsWith("['osg.Geometry']") || jToken.Path.EndsWith("['osgAnimation.MorphGeometry']"))
						{
							if (jToken.ToString().StartsWith("\"") || jToken.Path.Contains("MorphTargets"))
							{
								continue;
							}

							JObject geomObj = jToken.ToObject<JObject>();
							try
							{
								if (geomObj["PrimitiveSetList"].ToString().Contains("model_file_wireframe.bin.gz") || geomObj["PrimitiveSetList"].ToString().Contains("model_file_wireframe.binz"))
								{
									continue;
								}
							}
							catch {}

							bool hasMat = true;
							bool hasStateSet = true;
							try
							{
								string test = geomObj["StateSet"]["osg.StateSet"].ToString();
							}
							catch
							{
								hasStateSet = false;
							}
							bool isExistingMat = false;
							bool matFound = false;
							string matState;
							if (hasStateSet)
							{
								matState = geomObj["StateSet"]["osg.StateSet"]["UniqueID"].ToString();
							}
							else
							{
								matState = "0";
							}
							string matID = null;
							string matName = null;
							string meshName = null;
							if (!materialIDs.Contains(matState))
							{
								try
								{
									foreach (var value in geomObj["StateSet"]["osg.StateSet"]["UserDataContainer"]["Values"])
									{
										try
										{
											string val = null;
											JObject valObj = value.ToObject<JObject>();
											if (valObj["Name"].ToString() == "UniqueID")
											{
												val = valObj["Value"].ToString();
											}
											matName = materialNames2[materialSSIDs2.ToList().IndexOf(val)];
											matID = materialIDs2[materialSSIDs2.ToList().IndexOf(val)];
											matFound = true;
											break;
										}
										catch {}
									}
								}
								catch
								{
								}

								if (!matFound || string.IsNullOrEmpty(matName))
								{
									if (materialNames2.Length == 1 && materialNames2[0] == "Scene - Root" && matState == "0")
									{
										matName = "Scene - Root";
									}
									else
									{
										try
										{
											matName = geomObj["StateSet"]["osg.StateSet"]["AttributeList"][0]["osg.Material"]["Name"].ToString();
										}
										catch
										{
											try
											{
												matName = geomObj["StateSet"]["osg.StateSet"]["Name"].ToString();
											}
											catch
											{
												try
												{
													matName = $"UniqueID_{geomObj["StateSet"]["osg.StateSet"]["AttributeList"][0]["osg.Material"]["UniqueID"]}";
												}
												catch
												{
													try
													{
														matName = geomObj["Name"].ToString();
													}
													catch {}
												}
											}
										}
									}
								}
							}
							else
							{
								try
								{
									matName = materialNames[materialIDs.ToList().IndexOf(matState)];
									isExistingMat = true;
									matID = materialUIDs[materialIDs.ToList().IndexOf(matState)];
									matFound = true;
								}
								catch {}
							}

							if (string.IsNullOrEmpty(matName))
							{
								hasMat = false;
							}
							else
							{
								if (matName.Length > 60)
								{
									matName = matName.Substring(0, 60);
								}
								string matNameNew = matName;
								if (!isExistingMat)
								{
									int j = 0;
									while (materialNames.Contains(matNameNew))
									{
										matNameNew = $"{matName}_{j}";
										j++;
									}
									matName = matNameNew;
									Array.Resize(ref materialIDs, materialIDs.Length + 1);
									materialIDs[materialIDs.GetUpperBound(0)] = matState;
									Array.Resize(ref materialNames, materialNames.Length + 1);
									materialNames[materialNames.GetUpperBound(0)] = matName;
									if (matFound)
									{
										Array.Resize(ref materialUIDs, materialUIDs.Length + 1);
										materialUIDs[materialUIDs.GetUpperBound(0)] = matID;
									}
									else
									{
										Array.Resize(ref materialUIDs, materialUIDs.Length + 1);
										materialUIDs[materialUIDs.GetUpperBound(0)] = "";
									}
								}
							}
							try
							{
								meshName = geomObj["Name"].ToString();
							}
							catch
							{
								meshName = geomObj["UniqueID"].ToString();
							}

							string meshNameNew = null;
							if (string.IsNullOrEmpty(meshName))
							{
								continue;
							}
							if (meshName.Length > 60)
							{
								meshName = meshName.Substring(0, 60);
							}
							meshNameNew = meshName;
							int i = 0;
							while (meshNames.Contains(meshNameNew))
							{
								meshNameNew = $"{meshName}_{i}";
								i++;
							}
							meshName = meshNameNew;
							Array.Resize(ref meshNames, meshNames.Length + 1);
							meshNames[meshNames.GetUpperBound(0)] = meshName;

							try
							{
								if (geomObj["PrimitiveSetList"].ToString().Contains("\"Mode\": \"LINES\""))
								{
									continue;
								}
							}
							catch {}

							if (hasMat)
							{
								File.AppendAllText($"{modelPath}\\materialInfo.txt", $"{Environment.NewLine}Mesh \"{meshName}\" uses material \"{matName}\" and has UniqueID \"{geomObj["UniqueID"]}\"");
								SketchfabDL.progressMeshCount += 1;
							}
							else
							{
								File.AppendAllText($"{modelPath}\\materialInfo.txt", $"{Environment.NewLine}Mesh \"{meshName}\" has no material assigned, and has UniqueID \"{geomObj["UniqueID"]}\"");
								SketchfabDL.progressMeshCount += 1;
							}
						}

					}
					catch {}
				}

				string[] materialJSONIDs = {};
				foreach (var jToken in modelInfo["options"]["materials"])
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					try
					{
						Array.Resize(ref materialJSONIDs, materialJSONIDs.Length + 1);
						materialJSONIDs[materialJSONIDs.GetUpperBound(0)] = jToken.Path.Replace("options.materials.", "");
					}
					catch {}
				}

				int k = 0;
				string[] matIDsDone = {};
				foreach (var materialName in materialNames)
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					try
					{
						string materialID = null;
						try
						{
							materialID = materialUIDs[materialNames.ToList().IndexOf(materialName)];
						}
						catch
						{
						}

						if (string.IsNullOrEmpty(materialID))
						{
							foreach (var id in materialJSONIDs)
							{
								try
								{
									string matName = modelInfo["options"]["materials"][id]["name"].ToString();
									if (matName == materialName)
									{
										materialID = id;
									}
								}
								catch {}
							}
						}

						if (matIDsDone.Contains(materialID))
						{
							continue;
						}

						if (k == 0)
						{
							File.AppendAllText($"{modelPath}\\materialInfo.txt", Environment.NewLine);
						}

						if (string.IsNullOrEmpty(materialID))
						{
							foreach (var jToken in modelInfo["options"]["materials"])
							{
								try
								{
									if (jToken["name"].ToString() == materialName)
									{
										materialID = jToken.ToString();
									}
								}
								catch {}
							}
						}
						
						File.AppendAllText($"{modelPath}\\materialInfo.txt", $"{Environment.NewLine}Material \"{materialName}\" has ID {materialID}.{Environment.NewLine}");
						try
						{
							if (modelInfo["options"]["shading"]["vertexColor"]["enable"].ToString() == "True")
							{
								File.AppendAllText($"{modelPath}\\materialInfo.txt", $"\tVertex colour: Enabled{Environment.NewLine}");
							}
						}
						catch {}

						GetTextureName(modelInfo, materialID, modelPath, "\tAO: ", "AOPBR", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tBump map: ", "BumpMap", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tOpacity: ", "Opacity", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tAlbedo: ", "AlbedoPBR", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tCavity: ", "CavityPBR", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tClear coat: ", "ClearCoat", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tEmission: ", "EmitColor", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tNormal: ", "NormalMap", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tAnisotropy: ", "Anisotropy", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tDiffuse: ", "DiffusePBR", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tSpecular F0: ", "SpecularF0", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tSpecularPBR: ", "SpecularPBR", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tDiffuse colour: ", "DiffuseColor", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tDisplacement: ", "Displacement", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tMetalness: ", "MetalnessPBR", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tRoughness: ", "RoughnessPBR", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tGlossiness: ", "GlossinessPBR", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tSpecular colour: ", "SpecularColor", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tDiffuse intensity: ", "DiffuseIntensity", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tSpecular hardness: ", "SpecularHardness", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tClear coat normal: ", "ClearCoatNormalMap", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tClear coat roughness: ", "ClearCoatRoughness", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tSubsurface scattering: ", "SubsurfaceScattering", bw, token);
						GetTextureName(modelInfo, materialID, modelPath, "\tSubsurface translucency: ", "SubsurfaceTranslucency", bw, token);
						Array.Resize(ref matIDsDone, matIDsDone.Length + 1);
						matIDsDone[matIDsDone.GetUpperBound(0)] = materialID;
						colourDone = false;
						k++;
					}
					catch {}
				}
				var lines = File.ReadAllLines($"{modelPath}\\materialInfo.txt").ToList();
				foreach (var line in lines.ToList())
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					if (line.StartsWith("Material \""))
					{
						break;
					}
					if (Regex.IsMatch(line, "^(?!.*uses material.*).+$") && Regex.IsMatch(line, "^(?!.*has no material.*).+$") && Regex.IsMatch(line, "^(?!.*Generated using SketchfabRipper.*).+$"))
					{
						lines.RemoveAt(lines.IndexOf(line));
					}
					if (line.Contains("uses material \"\""))
					{
						lines.RemoveAt(lines.IndexOf(line));
					}
				}
				foreach (var line in lines.ToList())
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					if (line == "Material \"\" has ID .")
					{
						lines.RemoveAt(lines.IndexOf(line) + 1);
						lines.RemoveAt(lines.IndexOf(line));
					}
				}
				File.WriteAllLines($"{modelPath}\\materialInfo.txt", lines);
			}
			catch (Exception e)
			{
				bw.ReportProgress(-1, $"MessageBox:{e}");
			}
		}

		private static void VertexColourDumper(string modelPath, CancellationToken token)
		{
			try
			{
				JObject osgjs = JObject.Parse(File.ReadAllText($"{modelPath}\\file.osgjs"));
				var list = osgjs.DescendantsAndSelf();
				string itemSize = null;
				string meshID = "";
				foreach (var jToken in list)
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					try
					{
						if (jToken.Path.EndsWith("['osg.Geometry']"))
						{
							if (jToken.ToString().StartsWith("\""))
							{
								continue;
							}
							try
							{
								JObject geomObj = jToken.ToObject<JObject>();
								meshID = geomObj["UniqueID"].ToString();
							}
							catch {}
						}
						if (jToken.Path.Contains("VertexAttributeList.Color") && string.IsNullOrEmpty(itemSize))
						{
							try
							{
								if (jToken.ToString().StartsWith("\""))
								{
									continue;
								}
								itemSize = Regex.Match(jToken.ToString(), "(?<=\"ItemSize\": )\\d+").ToString();
							}
							catch {}
						}
						if (jToken.Path.Contains("VertexAttributeList.Color") && jToken.Path.EndsWith("Array") && !jToken.Path.EndsWith(".Array"))
						{
							if (jToken.ToString().StartsWith("\""))
							{
								continue;
							}
							if (jToken.Path.Contains("Float32Array"))
							{
								byte[] colourData = File.ReadAllBytes($"{modelPath}\\model_file.bin");
								colourData = colourData.Skip(int.Parse(jToken["Offset"].ToString())).Take(int.Parse(jToken["Size"].ToString()) * 16).ToArray();
								Directory.CreateDirectory($"{modelPath}\\vertexColourData");
								File.WriteAllBytes($"{modelPath}\\vertexColourData\\Mesh_{meshID}_Float32Array_{itemSize}.dat", colourData);
								itemSize = null;
							}
							if (jToken.Path.Contains("Uint8Array"))
							{
								byte[] colourData = File.ReadAllBytes($"{modelPath}\\model_file.bin");
								colourData = colourData.Skip(int.Parse(jToken["Offset"].ToString())).Take(int.Parse(jToken["Size"].ToString()) * 4).ToArray();
								Directory.CreateDirectory($"{modelPath}\\vertexColourData");
								File.WriteAllBytes($"{modelPath}\\vertexColourData\\Mesh_{meshID}_Uint8Array_{itemSize}.dat", colourData);
								itemSize = null;
							}
						}
					}
					catch {}
				}
			}
			catch {}
		}

		private static void KillProcessByName(string name)
		{
			try
			{
				var processes = Process.GetProcesses().Where(pr => pr.ProcessName == name);
				foreach (var process in processes)
				{
					process.Kill();
				}
			}
			catch (Exception e)
			{
				Logger.Log($"Failed to kill process: \"{name}\"", Logger.ErrorLevel.Error, exception: e.ToString());
			}
		}

		public static void SketchfabDumper(string modelID, Dictionary<string, string> options, BackgroundWorker bw, CancellationToken token, string sfToken = null, string sfSessionID = null)
		{
			bool animMode = false;
			bool skipUnused = false;
			string sfAuthData = null;
			string sfAuthType = null;

			Logger.Log("Dumping model resources...", Logger.ErrorLevel.Info);
			
			textureMode = options["TextureMode"];
			if (bool.Parse(options["AppendUniqueIDs"]))
			{
				uniqueMode = true;
			}
			if (options["ConversionMode"] == "Animated")
			{
				animMode = true;
			}
			if (options["FilesToDelete"] != "Nothing")
			{
				skipUnused = true;
			}
			if (!string.IsNullOrEmpty(sfToken))
			{
				sfAuthData = sfToken;
				sfAuthType = "AccessToken";
			}
			if (!string.IsNullOrEmpty(sfSessionID))
			{
				sfAuthData = sfSessionID;
				sfAuthType = "SessionID";
			}

			var webClient = new WebClient();
			string modelJSON;
			if (!SketchfabDL.isLocked) modelJSON = webClient.DownloadString($"https://sketchfab.com/i/models/{modelID}");
			else modelJSON = SketchfabDL.LockedModelRequest($"https://sketchfab.com/i/models/{modelID}");
			JObject jObject = JObject.Parse(modelJSON);
			string osgjsURL = jObject["files"][0]["osgjsUrl"].ToString();
			string modelName = jObject["slug"].ToString();
			if (string.IsNullOrEmpty(modelName.Replace("-", "").Replace(".", "").Replace("_", "")))
			{
				modelName = jObject["name"].ToString();
			}
			if (uniqueMode)
			{
				modelName = $"{modelName}_{modelID}";
			}
			string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
			foreach (char c in invalidChars)
			{
				modelName = modelName.Replace(c, '_');
			}

			Directory.CreateDirectory($"{Environment.CurrentDirectory}\\downloads\\{modelName}");

			if (token.IsCancellationRequested)
			{
				return;
			}

			string modelInfoJSON = modelJSON;
			string textureInfoJSON;
			if (!SketchfabDL.isLocked) textureInfoJSON = webClient.DownloadString($"https://sketchfab.com/i/models/{modelID}/textures");
			else textureInfoJSON = SketchfabDL.LockedModelRequest($"https://sketchfab.com/i/models/{modelID}/textures");
			modelInfoJSON = BeautifyJSON(modelInfoJSON);
			textureInfoJSON = BeautifyJSON(textureInfoJSON);
			File.WriteAllText($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_info.json", modelInfoJSON);
			File.WriteAllText($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\viewer_info.json", BeautifyJSON(viewerData.ToString()));
			if (!skipUnused)
			{
				File.WriteAllText($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\texture_info.json", textureInfoJSON);
			}

			isEncrypted = false;
			if (osgjsURL.EndsWith(".binz"))
			{
				isEncrypted = true;
				Logger.Log("Model uses the new .binz format, decryption is necessary.", Logger.ErrorLevel.Info);
			}
			string baseDataURL;
			if (isEncrypted)
			{
				baseDataURL = osgjsURL.Replace("file.binz", "");
			}
			else
			{
				baseDataURL = osgjsURL.Replace("file.osgjs.gz", "");
			}
			string modelFileURL = $"{baseDataURL}model_file.bin.gz";
			string modelFileWireframeURL = $"{baseDataURL}model_file_wireframe.bin.gz";
			string modelWireframeURL = $"{baseDataURL}model_wireframe.bin.gz";
			try
			{
				if (isEncrypted)
				{
					Directory.CreateDirectory($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}");
					try
					{
						try
						{
							bool matchFound = false;
							string sfScriptKey = null;
							string sfScriptName;
							string mPageData;
							if (!SketchfabDL.isLocked) mPageData = webClient.DownloadString($"https://sketchfab.com/models/{modelID}/embed");
							else mPageData = SketchfabDL.LockedModelRequest($"https://sketchfab.com/models/{modelID}/embed");
							// REMOVED: Extract key from viewer (sfScriptKey) and save to options["SFConfig"] for later use.
							try
							{
								File.WriteAllText($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\key2.txt", sfScriptKey);
							}
							catch {}
						}
						catch { }
						bw.ReportProgress(-1, "Downloading \"file.binz\"");
						webClient.DownloadFile($"{baseDataURL}file.binz", $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\file.binz");
						bw.ReportProgress((int)Math.Round(SketchfabDL.progress += 1.0));
						bw.ReportProgress(-1, "Downloading \"model_file.binz\"");
						webClient.DownloadFile($"{baseDataURL}model_file.binz", $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file.binz");
						bw.ReportProgress((int)Math.Round(SketchfabDL.progress += 1.0));
						bw.ReportProgress(-1, "Downloading \"model_file_wireframe.binz\"");
						webClient.DownloadFile($"{baseDataURL}model_file_wireframe.binz", $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file_wireframe.binz");
						bw.ReportProgress((int)Math.Round(SketchfabDL.progress += 2.0));
					}
					catch {}
				}
				else
				{
					bw.ReportProgress(-1, "Downloading \"file.osgjs.gz\"");
					webClient.DownloadFile(osgjsURL, $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\file.osgjs.gz");
					bw.ReportProgress((int)Math.Round(SketchfabDL.progress += 1.0));
					bw.ReportProgress(-1, "Downloading \"model_file.bin.gz\"");
					webClient.DownloadFile(modelFileURL, $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file.bin.gz");
					bw.ReportProgress((int)Math.Round(SketchfabDL.progress += 1.0));
					try
					{
						bw.ReportProgress(-1, "Downloading \"model_file_wireframe.bin.gz\"");
						webClient.DownloadFile(modelFileWireframeURL, $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file_wireframe.bin.gz");
					}
					catch {}

					SketchfabDL.progress += 1.0;
					bw.ReportProgress((int)Math.Round(SketchfabDL.progress));
					try
					{
						bw.ReportProgress(-1, "Downloading \"model_wireframe.bin.gz\"");
						webClient.DownloadFile(modelWireframeURL, $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_wireframe.bin.gz");
					}
					catch {}

					SketchfabDL.progress += 1.0;
					bw.ReportProgress((int)Math.Round(SketchfabDL.progress));
					bw.ReportProgress(-1, "Decompressing \"file.osgjs.gz\"");
					DecompressGZip($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\file.osgjs.gz");
					bw.ReportProgress(-1, "Decompressing \"model_file.bin.gz\"");
					DecompressGZip($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file.bin.gz");
					try
					{
						bw.ReportProgress(-1, "Decompressing \"model_file_wireframe.bin.gz\"");
						DecompressGZip($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file_wireframe.bin.gz");
					}
					catch {}

					try
					{
						bw.ReportProgress(-1, "Decompressing \"model_wireframe.bin.gz\"");
						DecompressGZip($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_wireframe.bin.gz");
					}
					catch {}
				}
				SketchfabDL.progress += 1.0;
				bw.ReportProgress((int)Math.Round(SketchfabDL.progress));
			}
			catch {}
			
			FileUtils.DeleteFile($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\file.osgjs.gz");
			FileUtils.DeleteFile($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file.bin.gz");
			FileUtils.DeleteFile($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file_wireframe.bin.gz");
			FileUtils.DeleteFile($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_wireframe.bin.gz");
			
			try
			{
				for (int i = 0;; i++)
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					try
					{
						string animBaseURL = Regex.Replace(baseDataURL, "files.*", "");
						string animName = viewerData["options"]["animation"]["order"][i].ToString();
						bw.ReportProgress(-1, $"Downloading animation \"{animName}.bin.gz\"");
						try
						{
							if (!Directory.Exists($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\animations\\"))
							{
								Directory.CreateDirectory($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\animations\\");
							}
						}
						catch {}
						webClient.DownloadFile($"{animBaseURL}animations/{animName}.bin.gz", $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\animations\\{animName}.bin.gz");
						//AppendToFileMap($"animations\\{animName}.bin.gz", $"{animBaseURL}animations/{animName}.bin.gz");
					}
					catch
					{
						break;
					}
				}
				foreach (string file in Directory.GetFiles($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\animations\\").Where(file => file.EndsWith(".gz")))
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					bw.ReportProgress(-1, $"Decompressing \"{Path.GetFileName(file)}\"");
					DecompressGZip(file);
					if (!animMode)
					{
						File.Delete(file);
					}
				}

				foreach (string file in Directory.GetFiles($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\animations\\").Where(file => file.EndsWith(".bin")))
				{
					try
					{
						File.Move(file, $"{file}.gz");
					}
					catch {}
				}
			}
			catch {}

			if (options["FilesToDelete"] == "Nothing" || options["FilesToDelete"] == "Extras" || options["FilesToDelete"] == "Generated")
			{
				GetSounds(modelID, $"{Environment.CurrentDirectory}\\downloads\\{modelName}", bw, token);
				bw.ReportProgress((int)Math.Round(SketchfabDL.progress += 0.5));
				try
				{
					GetEnvironment(viewerData["options"]["environment"]["uid"].ToString(), $"{Environment.CurrentDirectory}\\downloads\\{modelName}", bw, token);
				}
				catch {}
				bw.ReportProgress((int)Math.Round(SketchfabDL.progress += 2.0));
				try
				{
					GetBackground(viewerData["options"]["background"]["uid"].ToString(), $"{Environment.CurrentDirectory}\\downloads\\{modelName}", bw, token);
				}
				catch {}
				bw.ReportProgress((int)Math.Round(SketchfabDL.progress += 0.5));
				try
				{
					GetARData(modelID, $"{Environment.CurrentDirectory}\\downloads\\{modelName}", bw, token);
				}
				catch {}
				bw.ReportProgress((int)Math.Round(SketchfabDL.progress += 2.0));
			}

			try
			{
				bw.ReportProgress(-1, "Downloading thumbnail");
				var thumbData = Regex.Matches(modelJSON, "(?<=thumbnails)(.*?)(?=}]})");
				var thumbSizes = Regex.Matches(thumbData[0].Value, "(?<=size\":)(\\d+)").Cast<Match>().Select(m => m.Value).ToArray();
				var thumbURLs = Regex.Matches(thumbData[0].Value, "(?<=\"url\":\")(.*?)(\")").Cast<Match>().Select(m => m.Value).ToArray();

				int maxValue = 0;
				foreach (var i in thumbSizes)
				{
					int num = int.Parse(i);
					if (num > maxValue)
					{
						maxValue = num;
					}
				}
				int maxIndex = thumbSizes.ToList().IndexOf(maxValue.ToString());
				webClient.DownloadFile(thumbURLs.ElementAt(maxIndex).Replace("\\", "").Replace("\"", ""), $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\{modelName}.thumb.jpeg");
			}
			catch {}
			
			bw.ReportProgress((int)Math.Round(SketchfabDL.progress = 10.0));

			try
			{
				string textureJSON;
				if (!SketchfabDL.isLocked) textureJSON = webClient.DownloadString($"https://sketchfab.com/i/models/{modelID}/textures");
				else textureJSON = SketchfabDL.LockedModelRequest($"https://sketchfab.com/i/models/{modelID}/textures");
				jObject = JObject.Parse(textureJSON);
				string[] textureNames = { };
				string[] textureUIDs = { };
				string[] texturePKs = { };
				string[] texturePVs = { };
				for (int i = 0;; i++)
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					try
					{
						string textureName = jObject["results"][i]["name"].ToString();
						string textureUID = jObject["results"][i]["uid"].ToString();
						string texturePK = jObject["results"][i]["pk"].ToString();
						string texturePV = jObject["results"][i]["pv"].ToString();
						Array.Resize(ref textureNames, textureNames.Length + 1);
						textureNames[textureNames.GetUpperBound(0)] = textureName;
						Array.Resize(ref textureUIDs, textureUIDs.Length + 1);
						textureUIDs[textureUIDs.GetUpperBound(0)] = textureUID;
						Array.Resize(ref texturePKs, texturePKs.Length + 1);
						texturePKs[texturePKs.GetUpperBound(0)] = texturePK;
						Array.Resize(ref texturePVs, texturePVs.Length + 1);
						texturePVs[texturePVs.GetUpperBound(0)] = texturePV;
					}
					catch
					{
						break;
					}
				}

				try
				{
					var list = viewerData.DescendantsAndSelf();
					foreach (var jToken in list)
					{
						if (token.IsCancellationRequested)
						{
							return;
						}
						if (jToken.Path.Contains("materials") && jToken.Path.EndsWith(".uid") && !jToken.Path.Contains("Matcap"))
						{
							if (jToken.ToString().StartsWith("\""))
							{
								continue;
							}
							if (!textureUIDs.Contains(jToken.ToString()))
							{
								try
								{
									var objTmp = JObject.Parse(webClient.DownloadString($"https://sketchfab.com/i/textures/{jToken}"));
									var textureName = objTmp["name"].ToString();
									string texturePK = objTmp["images"][0]["pk"].ToString();
									string texturePV = objTmp["images"][0]["pv"].ToString();
									Array.Resize(ref textureNames, textureNames.Length + 1);
									textureNames[textureNames.GetUpperBound(0)] = textureName;
									Array.Resize(ref textureUIDs, textureUIDs.Length + 1);
									textureUIDs[textureUIDs.GetUpperBound(0)] = jToken.ToString();
									Array.Resize(ref texturePKs, texturePKs.Length + 1);
									texturePKs[texturePKs.GetUpperBound(0)] = texturePK;
									Array.Resize(ref texturePVs, texturePVs.Length + 1);
									texturePVs[texturePVs.GetUpperBound(0)] = texturePV;
								}
								catch (WebException) {}
							}
						}
					}
				}
				catch {}

				try
				{
					Directory.Delete($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\textures\\", true);
				}
				catch {}

				int index = 0;
				string[] downloadedHashes = {};
				string[] downloadedTextures = {};
				foreach (var textureName in textureNames)
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					try
					{
						bw.ReportProgress((int)Math.Round(SketchfabDL.progress += 15.0 * (1 / (double)textureUIDs.Length)));
						Directory.CreateDirectory($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\textures\\");
						if (!downloadedUIDs.Contains(textureUIDs[index]))
						{
							bw.ReportProgress(-1, $"Downloading \"{textureName}\"");
							var textureInfo = GetLargestTexture(textureUIDs[index], token).Split('|');
							string textureURL = textureInfo[1];
							int[] textureRes = { int.Parse(textureInfo[0].Split('x')[0]), int.Parse(textureInfo[0].Split('x')[1])};
							if (string.IsNullOrEmpty(textureURL) && token.IsCancellationRequested)
							{
								return;
							}

							string fileName = Path.GetFileNameWithoutExtension(textureName);
							string fileExtension = Path.GetExtension(textureName);
							if (textureMode == "AppendID")
							{
								fileName = $"{Path.GetFileNameWithoutExtension(textureName)}.{textureUIDs[index]}";
							}
							else if (textureMode == "AppendNum")
							{
								int i = 2;
								while (File.Exists($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\textures\\{fileName}{fileExtension}"))
								{
									fileName = $"{Path.GetFileNameWithoutExtension(textureName)}.{i}";
									i++;
								}
							}
							var httpClient = new HttpClient();
							var request = new HttpRequestMessage(HttpMethod.Get, textureURL);
							Stream contentStream = httpClient.SendAsync(request, token).Result.Content.ReadAsStreamAsync().Result;
							using (Stream stream = new FileStream($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\textures\\{fileName}{fileExtension}", FileMode.Create, FileAccess.Write, FileShare.None, 10000, false))
							{
								contentStream.CopyTo(stream);
							}
							contentStream.Flush();
							contentStream.Close();
							var fileStream = File.OpenRead($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\textures\\{fileName}{fileExtension}");
							byte[] fileHash = MD5.Create().ComputeHash(fileStream);
							fileStream.Close();
							string fileHashB64 = Convert.ToBase64String(fileHash);
							bool isDuplicate = false;
							for (int j = 0; j < downloadedHashes.Length; j++)
							{
								if (downloadedHashes[j] == fileHashB64 && downloadedTextures[j] == textureName)
								{
									isDuplicate = true;
									break;
								}
							}
							if (downloadedHashes.Contains(fileHashB64) && downloadedTextures.Contains(textureName) && isDuplicate)
							{
								bw.ReportProgress(-1, $"\"{textureName}\" is a duplicate, deleting...");
								File.Delete($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\textures\\{fileName}{fileExtension}");
							}
							else
							{
								Array.Resize(ref downloadedHashes, downloadedHashes.Length + 1);
								downloadedHashes[downloadedHashes.GetUpperBound(0)] = fileHashB64;
								Array.Resize(ref downloadedTextures, downloadedTextures.Length + 1);
								downloadedTextures[downloadedTextures.GetUpperBound(0)] = textureName;

								Array.Resize(ref matUIDs, matUIDs.Length + 1);
								matUIDs[matUIDs.GetUpperBound(0)] = textureUIDs[index];
								Array.Resize(ref matTextures, matTextures.Length + 1);
								matTextures[matTextures.GetUpperBound(0)] = fileName + fileExtension;

								//AppendToFileMap($"textures\\{fileName}{fileExtension}", textureURL);
							}
							Array.Resize(ref downloadedUIDs, downloadedUIDs.Length + 1);
							downloadedUIDs[downloadedUIDs.GetUpperBound(0)] = textureUIDs[index];

							if (texturePKs[index] != "null" && !SketchfabDL.isPublicBuild)
							{
								// REMOVED: Texture unscrambling
							}
						}
						else
						{
							bw.ReportProgress(-1, $"File \"{textureName}\" already exists, skipping");
						}
						index++;
					}
					catch {}
				}
			}
			catch {}
			
			bw.ReportProgress((int)Math.Round(SketchfabDL.progress = 25.0));

			try
			{
				string url = JObject.Parse(File.ReadAllText($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_info.json"))["viewerUrl"].ToString();
				if (!string.IsNullOrEmpty(url))
				{
					StreamWriter writer = new StreamWriter($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\url.url");
					writer.WriteLine("[InternetShortcut]");
					writer.WriteLine($"URL={url}");
					writer.Flush();
					writer.Dispose();
				}
			}
			catch {}

			try
			{
				jObject = JObject.Parse(modelJSON);
				if ((bool.Parse(jObject["isDownloadable"].ToString()) && jObject["downloadType"].ToString() == "free") || (jObject["downloadType"].ToString() == "store"))
				{
					if (!string.IsNullOrEmpty(sfAuthType))
					{ 
						bw.ReportProgress(-1, "Downloading original model data...");
						Directory.CreateDirectory($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\original\\");
						try
						{
							if (sfAuthType == "AccessToken")
							{
								HttpWebRequest httpWebRequest = WebRequest.CreateHttp($"https://api.sketchfab.com/v3/models/{modelID}/download");
								httpWebRequest.Method = "GET";
								httpWebRequest.Headers.Add("Authorization", $"Bearer {sfAuthData}");
								httpWebRequest.Timeout = 4000;
								httpWebRequest.Proxy = null;
								var responseStream = httpWebRequest.GetResponse().GetResponseStream();
								StreamReader readStream = new StreamReader(responseStream, Encoding.UTF8);
								JObject downloadInfo = JObject.Parse(readStream.ReadToEnd());
								Uri fileUrl = new Uri(downloadInfo["gltf"]["url"].ToString());
								string fileName = Path.GetFileName(fileUrl.LocalPath);
								webClient.DownloadFile(fileUrl, $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\original\\{fileName}");
								fileUrl = new Uri(downloadInfo["usdz"]["url"].ToString());
								fileName = Path.GetFileName(fileUrl.LocalPath);
								webClient.DownloadFile(fileUrl, $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\original\\{fileName}");
							}
							else if (sfAuthType == "SessionID")
							{
								CookieContainer cookieContainer = new CookieContainer();
								cookieContainer.Add(new Cookie("sb_sessionid", sfAuthData, "/", "sketchfab.com"));
								HttpWebRequest httpWebRequest = WebRequest.CreateHttp($"https://sketchfab.com/i/archives/latest?archive_type=source&model={modelID}");
								httpWebRequest.Method = "GET";
								httpWebRequest.CookieContainer = cookieContainer;
								httpWebRequest.Timeout = 4000;
								var responseStream = httpWebRequest.GetResponse().GetResponseStream();
								StreamReader readStream = new StreamReader(responseStream, Encoding.UTF8);
								JObject downloadInfo = JObject.Parse(readStream.ReadToEnd());
								Uri fileUrl = new Uri(downloadInfo["url"].ToString());
								string fileName = Path.GetFileName(fileUrl.LocalPath);
								webClient.DownloadFile(fileUrl, $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\original\\{fileName}");
							}
						}
						catch {}
					}
				}
			}
			catch {}

			Logger.Log("Successfully downloaded model data.", Logger.ErrorLevel.Info);
			/*try
			{
				if (isEncrypted)
				{
					var processes = Process.GetProcesses().Where(pr => pr.ProcessName == "nginx-sketchfab");
					if (processes.ToArray().Length == 0)
					{
						using (TcpClient tcpClient = new TcpClient())
						{
							try
							{
								if (!bool.Parse(options["SilentMode"]))
								{
									tcpClient.Connect("127.0.0.1", 90);
									bw.ReportProgress(-1, "MessageBox:Port 90 is in use, nginx cannot start. Please close any software which is using this port before continuing.");
								}
							}
							catch {}
						}
					}

					bw.ReportProgress(-1, "Dumping .binz files...");
					Process nginx = new Process();
					nginx.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\binzDumper\\nginx\\nginx-sketchfab.exe";
					nginx.StartInfo.WorkingDirectory = $"{Environment.CurrentDirectory}\\tools\\binzDumper\\nginx";
					nginx.StartInfo.UseShellExecute = false;
					nginx.StartInfo.CreateNoWindow = true;
					nginx.Start();
					SketchfabDL.pid_nginx = nginx.Id;
					Thread.Sleep(1000);

					if (token.IsCancellationRequested)
					{
						KillProcessByName("nginx-sketchfab");
						KillProcessByName("chromedriver");
						KillProcessByName("chrome-sketchfab");
						return;
					}

					Process binzDumper = new Process();
					binzDumper.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\binzDumper\\binzDumper.exe";
					binzDumper.StartInfo.WorkingDirectory = $"{Environment.CurrentDirectory}\\tools\\binzDumper";
					binzDumper.StartInfo.Arguments = modelID;
					binzDumper.StartInfo.UseShellExecute = false;
					binzDumper.StartInfo.CreateNoWindow = true;
					binzDumper.Start();
					SketchfabDL.pid_binzDumper = binzDumper.Id;
					binzDumper.WaitForExit();
					KillProcessByName("nginx-sketchfab");
					KillProcessByName("chromedriver");
					KillProcessByName("chrome-sketchfab");
					
					if (!File.Exists($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\file.osgjs"))
					{
						if (File.Exists($"{Environment.CurrentDirectory}\\tools\\binzDumper\\downloads\\file.osgjs") && File.Exists($"{Environment.CurrentDirectory}\\tools\\binzDumper\\downloads\\model_file.bin"))
						{
							if (!Directory.Exists($"{Environment.CurrentDirectory}\\downloads\\{modelName}") && File.Exists($"{Environment.CurrentDirectory}\\tools\\binzDumper\\nginx\\data\\modelData\\file.binz"))
							{
								FileUtils.MoveDir($"{Environment.CurrentDirectory}\\tools\\binzDumper\\nginx\\data\\modelData", $"{Environment.CurrentDirectory}\\downloads\\{modelName}");
							}
							FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\binzDumper\\downloads\\file.osgjs", $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\file.osgjs");
							FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\binzDumper\\downloads\\model_file.bin", $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file.bin");
						}

						if (!File.Exists($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\file.osgjs") || !File.Exists($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file.bin") || !File.Exists($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\file.binz"))
						{
							Logger.Log("An error occurred while dumping the .binz files: \"file.osgjs\" could not be found.", Logger.ErrorLevel.Critical);
							bw.ReportProgress(-1, "MessageBox:\"file.osgjs\" could not be found, decryption has likely failed. In some cases, retrying the download may fix this. If not, compress the \"logs\" folder and post it on Discord along with a description of your issue.");
							SketchfabDL.decryptionFailed = true;
							return;
						}
					}
				}
			}
			catch (Exception e)
			{
				if (!bool.Parse(options["SilentMode"]))
				{
					Logger.Log("An exception was thrown while dumping the .binz files:", Logger.ErrorLevel.Error, exception: e.ToString());
					bw.ReportProgress(-1, $"MessageBox:{e}");
				}
			}*/

			if (isEncrypted)
			{
				bw.ReportProgress(-1, "Decrypting .binz files...");
				FileUtils.MoveFile($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\file.binz", $"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\file.binz");
				FileUtils.MoveFile($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file.binz", $"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\model_file.binz"); 
				FileUtils.MoveFile($"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file_wireframe.binz", $"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\model_file_wireframe.binz");
				File.WriteAllText($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\key.txt", viewerData["files"][0]["p"][0]["b"].ToString());
				Process binzDecrypt = new Process();
				binzDecrypt.StartInfo.FileName = $"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\binzDecrypt.exe";
				binzDecrypt.StartInfo.WorkingDirectory = $"{Environment.CurrentDirectory}\\tools\\binzDecrypt";
				binzDecrypt.StartInfo.UseShellExecute = false;
				binzDecrypt.StartInfo.CreateNoWindow = true;
				binzDecrypt.StartInfo.Arguments = $"_tmp\\{SketchfabDL.session}\\key.txt _tmp\\{SketchfabDL.session}\\file.binz";
				binzDecrypt.Start();
				binzDecrypt.WaitForExit();
				binzDecrypt.StartInfo.Arguments = $"_tmp\\{SketchfabDL.session}\\key.txt _tmp\\{SketchfabDL.session}\\model_file.binz";
				binzDecrypt.Start();
				binzDecrypt.WaitForExit();
				binzDecrypt.StartInfo.Arguments = $"_tmp\\{SketchfabDL.session}\\key.txt _tmp\\{SketchfabDL.session}\\model_file_wireframe.binz";
				binzDecrypt.Start();
				binzDecrypt.WaitForExit();
				FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\file.osgjs", $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\file.osgjs");
				FileUtils.DeleteFile($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\file.binz");
				FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\model_file.bin", $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file.bin");
				FileUtils.DeleteFile($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\model_file.binz");
				FileUtils.MoveFile($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\model_file_wireframe.bin", $"{Environment.CurrentDirectory}\\downloads\\{modelName}\\model_file_wireframe.bin");
				FileUtils.DeleteFile($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\model_file_wireframe.binz");
				FileUtils.DeleteFile($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\key.txt");
				FileUtils.DeleteFile($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}\\key2.txt");
				FileUtils.DeleteDir($"{Environment.CurrentDirectory}\\tools\\binzDecrypt\\_tmp\\{SketchfabDL.session}");
			}

			if (token.IsCancellationRequested)
			{
				return;
			}

			try
			{
				MaterialParser($"{Environment.CurrentDirectory}\\downloads\\{modelName}", bw, token);
			}
			catch (Exception e)
			{
				if (!bool.Parse(options["SilentMode"]))
				{
					bw.ReportProgress(-1, $"MessageBox:{e}");
				}
			}
			VertexColourDumper($"{Environment.CurrentDirectory}\\downloads\\{modelName}", token);
		}
	}
}
