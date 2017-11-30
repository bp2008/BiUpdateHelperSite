using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BiUpdateHelperSite.DB;
using BPUtil;
using BPUtil.SimpleHttp;
using Newtonsoft.Json;

namespace BiUpdateHelperSite
{
	public class WebServer : HttpServer
	{
		private long rnd = StaticRandom.Next(int.MinValue, int.MaxValue);
		public WebServer(int port) : base(port)
		{
			//SendBufferSize = 65536;
			XRealIPHeader = true;
		}
		private static bool IsAdmin(HttpProcessor p)
		{
			return p.RemoteIPAddress == MainStatic.settings.adminIp;
		}
		public override void handleGETRequest(HttpProcessor p)
		{
			string pageLower = p.requestedPage.ToLower();
			if (pageLower.StartsWith("api/"))
			{
				p.writeFailure("405 Method Not Allowed");
			}
			else if (p.requestedPage == "IP")
			{
				p.writeSuccess("text/plain");
				p.outputStream.Write(p.RemoteIPAddress);
			}
			else if (p.requestedPage == "HEADERS")
			{
				p.writeSuccess("text/plain");
				p.outputStream.Write(string.Join(Environment.NewLine, p.httpHeadersRaw.Select(h => h.Key + ": " + h.Value)));
			}
			else if (p.requestedPage == "IP")
			{
				p.writeSuccess("text/plain");
				p.outputStream.Write(p.RemoteIPAddress);
			}
			else if (p.requestedPage == "")
			{
				p.writeRedirect("default.html");
			}
			else
			{
				string wwwPath = Globals.ApplicationDirectoryBase + "www/";
#if DEBUG
				if (System.Diagnostics.Debugger.IsAttached)
					wwwPath = Globals.ApplicationDirectoryBase + "../../../www/";
#endif
				DirectoryInfo WWWDirectory = new DirectoryInfo(wwwPath);
				string wwwDirectoryBase = WWWDirectory.FullName.Replace('\\', '/').TrimEnd('/') + '/';
				FileInfo fi = new FileInfo(wwwDirectoryBase + p.requestedPage);
				string targetFilePath = fi.FullName.Replace('\\', '/');
				if (!targetFilePath.StartsWith(wwwDirectoryBase) || targetFilePath.Contains("../"))
				{
					p.writeFailure("400 Bad Request");
					return;
				}
				if (!fi.Exists)
					return;
				if ((fi.Extension == ".html" || fi.Extension == ".htm") && fi.Length < 256000)
				{
					string html = File.ReadAllText(fi.FullName);
					html = html.Replace("%%VERSION%%", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString());
					html = html.Replace("%%RND%%", rnd.ToString());
					html = html.Replace("%%ADMIN%%", IsAdmin(p) ? "true" : "false");

					byte[] data = Encoding.UTF8.GetBytes(html);
					p.writeSuccess(Mime.GetMimeType(fi.Extension), data.Length);
					p.outputStream.Flush();
					p.rawOutputStream.Write(data, 0, data.Length);
					p.rawOutputStream.Flush();
				}
				else
				{
					string mime = Mime.GetMimeType(fi.Extension);
					if (pageLower.StartsWith(".well-known/acme-challenge/"))
						mime = "text/plain";
					if (fi.LastWriteTimeUtc.ToString("R") == p.GetHeaderValue("if-modified-since"))
					{
						p.writeSuccess(mime, -1, "304 Not Modified");
						return;
					}
					p.writeSuccess(mime, fi.Length, additionalHeaders: GetCacheLastModifiedHeaders(TimeSpan.FromHours(1), fi.LastWriteTimeUtc));
					p.outputStream.Flush();
					using (FileStream fs = fi.OpenRead())
					{
						fs.CopyTo(p.rawOutputStream);
					}
					p.rawOutputStream.Flush();
				}
			}
		}
		private List<KeyValuePair<string, string>> GetCacheLastModifiedHeaders(TimeSpan maxAge, DateTime lastModifiedUTC)
		{
			List<KeyValuePair<string, string>> additionalHeaders = new List<KeyValuePair<string, string>>();
			additionalHeaders.Add(new KeyValuePair<string, string>("Cache-Control", "max-age=" + (long)maxAge.TotalSeconds + ", public"));
			additionalHeaders.Add(new KeyValuePair<string, string>("Last-Modified", lastModifiedUTC.ToString("R")));
			return additionalHeaders;
		}

		public override void handlePOSTRequest(HttpProcessor p, StreamReader inputData)
		{
			string pageLower = p.requestedPage.ToLower();
			if (pageLower.StartsWith("api/"))
			{
				string cmd = p.requestedPage.Substring("api/".Length);
				switch (cmd)
				{
					case "uploadUsageRecord1":
						{
							// If this needs extended in the future, just make a new "case" with an incremented number.
							string allUploaded = inputData.ReadToEnd();
							if (allUploaded.Length < 33)
								return;
							string md5 = allUploaded.Substring(allUploaded.Length - 32).ToLower();
							allUploaded = allUploaded.Remove(allUploaded.Length - 32);
							string md5Verify = Hash.GetMD5Hex(allUploaded);
							if (md5 != md5Verify)
							{
								p.writeFailure("400 Bad Request");
								return;
							}
							UsageRecordUpload.v1.Upload_Record record = JsonConvert.DeserializeObject<UsageRecordUpload.v1.Upload_Record>(allUploaded);
							DB.Agent.AddUsageRecord(record);
							p.writeSuccess("text/plain", 0);
						}
						break;
					case "getUsageRecords":
						{
							JSAPI.APIResponse response;
							int detailsForId = p.GetIntParam("detailsForId", -1);
							if (detailsForId > -1)
							{
								UsageRecord record = Agent.GetUsageRecord(detailsForId);
								if (record == null)
									response = new JSAPI.APIResponse("Could not find record \"" + detailsForId + "\"");
								else
								{
									List<Camera> cameras = Agent.GetCameras(record.ID);
									List<GpuInfo> gpus = Agent.GetGpus(record.ID);
									response = new JSAPI.DetailedRecordResponse(record, cameras, gpus);
									if (IsAdmin(p))
									{
										JSAPI.DetailedRecordResponse response2 = (JSAPI.DetailedRecordResponse)response;
										response2.secret = response2.usage.Secret;
									}
								}
							}
							else
							{
								response = new JSAPI.AllUsageRecordsResponse(Agent.GetAllUsageRecords_TimestampDesc());
								if (IsAdmin(p))
								{
									JSAPI.AllUsageRecordsResponse response2 = (JSAPI.AllUsageRecordsResponse)response;
									response2.secrets = response2.records.Select(r => new UsageRecord_Secret_Map() { ID = r.ID, Secret = r.Secret });
								}
							}
							byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response));
							p.writeSuccess("application/json", data.Length);
							p.outputStream.Flush();
							p.rawOutputStream.Write(data, 0, data.Length);
							p.rawOutputStream.Flush();
						}
						break;
					case "deleteRecord":
						{
							JSAPI.APIResponse response;
							if (!IsAdmin(p))
								response = new JSAPI.APIResponse("Unauthorized");
							else
							{
								int id = p.GetIntParam("id", -1);
								if (id < 0)
									response = new JSAPI.APIResponse("Invalid ID");
								else
								{
									bool success = Agent.DeleteUsageRecord(id);
									if (success)
										response = new JSAPI.APIResponse();
									else
										response = new JSAPI.APIResponse("Failed to delete record");
								}
							}
							byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response));
							p.writeSuccess("application/json", data.Length);
							p.outputStream.Flush();
							p.rawOutputStream.Write(data, 0, data.Length);
							p.rawOutputStream.Flush();
						}
						break;
				}
			}
		}

		protected override void stopServer()
		{
		}
	}
}
