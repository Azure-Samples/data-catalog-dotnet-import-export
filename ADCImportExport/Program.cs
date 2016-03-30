//Microsoft sample

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using System.Net;
using Newtonsoft.Json; //Install-Package Newtonsoft.Json
using Newtonsoft.Json.Linq;
using System.Security;
using Microsoft.IdentityModel.Clients.ActiveDirectory; //Install-Package Microsoft.IdentityModel


namespace ADCImportExport
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                System.Console.WriteLine("-import file_path or -export file_path [-search search_string]");

                Console.ReadLine();
                return;
            }

            string operation = args[0];
            string filePath = args[1];

            AzureDataCatalog td = new AzureDataCatalog();

            if (operation.Equals("-export"))
            {
                string searchString = "";

                if ((args.Length >= 4) && (args[2].ToLower().Equals("-search")))
                {
                    searchString = args[3];
                }

                using (StreamWriter sw = new StreamWriter(filePath, false))
                {
                    int count = Export(td, sw, searchString);
                    System.Console.WriteLine("Items Exported: " + count);
                }

            }
            else if (operation.Equals("-import"))
            {
                Import(td, filePath);
            }
            else
            {
                System.Console.WriteLine("Must specify either import or export");

            }

            Console.ReadLine();
        }

        static int Export(AzureDataCatalog td, StreamWriter sw, string searchString)
        {
            const int countPerPage = 100;

            bool firstTime = true;
            int startPage = 1;
            int totalResultsCount = 0;
            int totalExportedSuccessfully = 0;

            sw.Write("{\"catalog\":[");

            do
            {
                string results = td.Search(searchString, startPage, countPerPage);

                if (results == null)
                {
                    return 0;
                }

                if (firstTime)
                {
                    totalResultsCount = (int)JObject.Parse(results)["totalResults"];
                }

                var assetList = JObject.Parse(results)["results"].Children();

                foreach (JObject asset in assetList["content"])
                {
                    if (firstTime)
                    {
                        firstTime = false;
                    }
                    else
                    {
                        sw.Write(",");
                    }

                    (asset["properties"] as JObject).Remove("containerId");

                    var annotationsNode = asset.SelectToken("annotations") as JObject;

                    if (annotationsNode != null)
                    {
                        bool previewExists = annotationsNode.Remove("previews");
                        bool columnsDataProfilesExists = annotationsNode.Remove("columnsDataProfiles");
                        bool tableDataProfilesExist = annotationsNode.Remove("tableDataProfiles");

                        if (previewExists || columnsDataProfilesExists || tableDataProfilesExist)
                        {
                            var fullAsset = JObject.Parse(td.Get(asset["id"].ToString()));
                            if (previewExists)
                            {
                                annotationsNode.Add("previews", fullAsset["annotations"]["previews"]);
                            }
                            if (columnsDataProfilesExists)
                            {
                                annotationsNode.Add("columnsDataProfiles", fullAsset["annotations"]["columnsDataProfiles"]);
                            }
                            if (tableDataProfilesExist)
                            {
                                annotationsNode.Add("tableDataProfiles", fullAsset["annotations"]["tableDataProfiles"]);
                            }
                        }
                    }

                    //Set contributor equal to "Everyone" on all nodes. This allows them to be updated by others later.  Ideally we would preserve the contributor but that requires
                    //a special platform to enable it.

                    JToken contributor = JObject.Parse("{'role': 'Contributor','members': [{'objectId': '00000000-0000-0000-0000-000000000201'}]}");
                    var roles = new JArray();
                    roles.Add(contributor);

                    foreach (var rolesNode in asset.SelectTokens("$..roles").ToList())
                    {
                        rolesNode.Replace(roles);
                    }

                    RemoveSystemProperties(asset);

                    sw.Write(JsonConvert.SerializeObject(asset));
                    totalExportedSuccessfully++;
                    if (totalExportedSuccessfully % 10 == 0)
                    {
                        Console.Write(".");
                    }
                }

                startPage++;
            } while ((startPage - 1) * countPerPage < totalResultsCount);

            sw.Write("]}");

            Console.WriteLine("");

            return totalExportedSuccessfully;
        }

        static void Import(AzureDataCatalog td, string exportedCatalogFilePath)
        {
            int totalAssetsImportSucceeded = 0;
            int totalAssetsImportFailed = 0;

            System.IO.StreamReader sr = new StreamReader(exportedCatalogFilePath);
            JsonTextReader reader = new JsonTextReader(sr);

            StringWriter sw = new StringWriter(new StringBuilder());

            JsonTextWriter jtw = new JsonTextWriter(sw);

            reader.Read();
            if (reader.TokenType != JsonToken.StartObject)
            {
                throw new Exception("Invalid Json. Expected StartObject");
            }

            reader.Read();
            if ((reader.TokenType != JsonToken.PropertyName) || (!reader.Value.ToString().Equals("catalog")))
            {
                throw new Exception("Invalid Json. Expected catalog array");
            }

            reader.Read();
            if (reader.TokenType != JsonToken.StartArray)
            {
                throw new Exception("Invalid Json. Expected StartArray");
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndArray)
                    break;

                jtw.WriteToken(reader);

                JObject asset = JObject.Parse(sw.ToString());

                string id = asset["id"].ToString();
                asset.Remove("id");
                string[] idInfo = id.Split(new char[] { '/' });
                string newid;

                string UpdateResponse = td.Update(asset.ToString(), idInfo[idInfo.Length - 2], out newid);

                if ((UpdateResponse != null) && (!string.IsNullOrEmpty(newid)))
                {
                    totalAssetsImportSucceeded++;

                    if (totalAssetsImportSucceeded % 50 == 0)
                    {
                        System.Console.WriteLine(totalAssetsImportSucceeded + "Assets Imported Succesfully");
                    }
                }
                else
                {
                    totalAssetsImportFailed++;
                }

                //reset local variables for next iteration
                sw = new StringWriter(new StringBuilder());
                jtw = new JsonTextWriter(sw);

            }

            Console.WriteLine("Total Imported Success: " + totalAssetsImportSucceeded);
            Console.WriteLine("Total Imported Failed: " + totalAssetsImportFailed);
        }

        private static void RemoveSystemProperties(JObject payload, bool isRoot = true)
        {
            foreach (var propertyName in ((IDictionary<string, JToken>)payload).Keys.ToArray()
                .Where(k => k != "properties" && k != "annotations" && !(k == "id" && isRoot)))
            {
                payload.Remove(propertyName);
            }

            JToken annotationsJToken;
            if (payload.TryGetValue("annotations", out annotationsJToken) && annotationsJToken.Type == JTokenType.Object)
            {
                var annotationsJObject = (JObject)annotationsJToken;
                foreach (var jProperty in annotationsJObject.Properties())
                {
                    if (jProperty.Value.Type == JTokenType.Object)
                    {
                        var nextPayload = (JObject)jProperty.Value;
                        RemoveSystemProperties(nextPayload, false);
                        annotationsJObject[jProperty.Name] = nextPayload;
                    }
                    else if (jProperty.Value.Type == JTokenType.Array)
                    {
                        foreach (var jItem in ((JArray)jProperty.Value).Where(i => i.Type == JTokenType.Object))
                        {
                            RemoveSystemProperties((JObject)jItem, false);
                        }

                        annotationsJObject[jProperty.Name] = jProperty.Value;
                    }
                }
            }
        }

        public class AzureDataCatalog
        {
            private readonly string ClientId;
            private readonly Uri RedirectUri;
            private readonly string CatalogName;

            private static AuthenticationResult auth;
            private static readonly AuthenticationContext AuthContext = new AuthenticationContext("https://login.windows.net/common/oauth2/authorize");


            public AzureDataCatalog()
            {
                //NOTE: You must fill in the App.Config with the following three settings. The first two are values that you received registered your application with AAD. The 3rd setting is always the same value.:
                //< ADCImportExport.Properties.Settings >
                //    <setting name = "ClientId" serializeAs = "String">
                //           <value></value>
                //       </setting>
                //       <setting name = "RedirectURI" serializeAs = "String">
                //              <value> https://login.live.com/oauth20_desktop.srf</value>
                //    </setting>
                //    <setting name = "ResourceId" serializeAs = "String">
                //           <value> https://datacatalog.azure.com</value>
                //    </setting>
                //</ADCImportExport.Properties.Settings>

                ClientId = ADCImportExport.Properties.Settings.Default.ClientId;
                RedirectUri = new Uri(ADCImportExport.Properties.Settings.Default.RedirectURI);

                CatalogName = "DefaultCatalog";

                auth = AuthContext.AcquireToken("https://api.azuredatacatalog.com", ClientId, RedirectUri, PromptBehavior.Always);
            }

            public string Get(string uri)
            {

                var fullUri = string.Format("{0}?api-version=2016-03-30", uri);

                string requestId = null;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(fullUri);
                request.Method = "GET";

                try
                {
                    string s = GetPayload(request, out requestId);
                    return s;
                }
                catch (WebException ex)
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.Status);
                    Console.WriteLine("Request Id: " + requestId);

                    if (ex.Response != null)
                    {
                        // can use ex.Response.Status, .StatusDescription
                        if (ex.Response.ContentLength != 0)
                        {
                            using (var stream = ex.Response.GetResponseStream())
                            {
                                using (var reader = new StreamReader(stream))
                                {
                                    Console.WriteLine("Failed Get of asset: " + uri);
                                    Console.WriteLine(reader.ReadToEnd());
                                }
                            }
                        }
                    }
                    return null;
                }
            }

            public string Search(string searchTerm, int startPage, int count)
            {
                var fullUri = string.Format("https://api.azuredatacatalog.com/catalogs/{0}/search/search?searchTerms={1}&count={2}&startPage={3},&api-version=2016-03-30", CatalogName, searchTerm, count, startPage);

                string requestId = null;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(fullUri);
                request.Method = "GET";

                try
                {
                    string s = GetPayload(request, out requestId);
                    return s;
                }
                catch (WebException ex)
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.Status);
                    Console.WriteLine("Request Id: " + requestId);
                    if (ex.Response != null)
                    {
                        // can use ex.Response.Status, .StatusDescription
                        if (ex.Response.ContentLength != 0)
                        {
                            using (var stream = ex.Response.GetResponseStream())
                            {
                                using (var reader = new StreamReader(stream))
                                {
                                    Console.WriteLine(reader.ReadToEnd());
                                }
                            }
                        }
                    }
                    return null;
                }
            }

            public string Update(string postPayload, string viewType, out string id)
            {

                var fullUri = string.Format("https://api.azuredatacatalog.com/catalogs/{0}/views/{1}?api-version=2016-03-30", CatalogName, viewType);

                string requestId = null;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(fullUri);
                request.Method = "POST";
                try
                {
                    var response = SetRequestAndGetResponse(request, out requestId, postPayload);
                    var responseStream = response.GetResponseStream();

                    id = response.Headers["location"];

                    StreamReader reader = new StreamReader(responseStream);
                    return reader.ReadToEnd();
                }
                catch (WebException ex)
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.Status);
                    Console.WriteLine("Request Id: " + requestId);
                    if (ex.Response != null)
                    {
                        // can use ex.Response.Status, .StatusDescription
                        if (ex.Response.ContentLength != 0)
                        {
                            using (var stream = ex.Response.GetResponseStream())
                            {
                                using (var reader = new StreamReader(stream))
                                {
                                    Console.WriteLine("Failed Update of asset: " + postPayload.Substring(0, 50));
                                    Console.WriteLine(reader.ReadToEnd());
                                }
                            }
                        }
                    }
                    id = null;
                    return null;
                }
            }

            private static HttpWebResponse SetRequestAndGetResponse(HttpWebRequest request, out string requestId, string payload = null)
            {
                while (true)
                {
                    //Add a guid to help with diagnostics
                    requestId = Guid.NewGuid().ToString();
                    request.Headers.Add("x-ms-client-request-id", requestId);
                    //To authorize the operation call, you need an access token which is part of the Authorization header
                    request.Headers.Add("Authorization", auth.CreateAuthorizationHeader());
                    //Set to false to be able to intercept redirects
                    request.AllowAutoRedirect = false;

                    if (!string.IsNullOrEmpty(payload))
                    {
                        byte[] byteArray = Encoding.UTF8.GetBytes(payload);
                        request.ContentLength = byteArray.Length;
                        request.ContentType = "application/json";
                        //Write JSON byte[] into a Stream
                        request.GetRequestStream().Write(byteArray, 0, byteArray.Length);
                    }
                    else
                    {
                        request.ContentLength = 0;
                    }

                    HttpWebResponse response = request.GetResponse() as HttpWebResponse;

                    // Requests to **Azure Data Catalog (ADC)** may return an HTTP 302 response to indicate
                    // redirection to a different endpoint. In response to a 302, the caller must re-issue
                    // the request to the URL specified by the Location response header. 
                    if (response.StatusCode == HttpStatusCode.Redirect)
                    {
                        string redirectedUrl = response.Headers["Location"];
                        HttpWebRequest nextRequest = WebRequest.Create(redirectedUrl) as HttpWebRequest;
                        nextRequest.Method = request.Method;
                        request = nextRequest;
                    }
                    else
                    {
                        return response;
                    }
                }
            }

            private static string GetPayload(HttpWebRequest request, out string requestId)
            {
                string result = String.Empty;
                var response = SetRequestAndGetResponse(request, out requestId);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new ApplicationException("Request wrong");
                var stream = response.GetResponseStream();
                StreamReader reader = new StreamReader(stream);
                result = reader.ReadToEnd();
                return result;
            }
        }
    }
}