//Microsoft sample

using System;
using System.Text;
using System.IO;
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
                    int count = Export(td,sw, searchString);
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
                    asset.Remove("__permissions");
                    asset.Remove("__effectiveRights");
                    asset.Remove("__roles");
                    asset.Remove("__type");

                    //Set contributor equal to "Everyone" on all nodes. This allows them to be updated by others later.  Ideally we would preserve the contributor but that requires
                    //a special platform to enable it.

                    JToken contributor = JObject.Parse("{'role': 'Contributor','members': [{'objectId': '00000000-0000-0000-0000-000000000201'}]}");
                    JArray roles = new JArray();
                    roles.Add(contributor);
                    asset.Add("__roles", roles);

                    bool previewExists = asset.Remove("previews");
                    bool columnsDataProfilesExists = asset.Remove("columnsDataProfiles");
                    bool tableDataProfilesExist = asset.Remove("tableDataProfiles");

                    if (previewExists || columnsDataProfilesExists || tableDataProfilesExist)
                    {
                        var fullAsset = JObject.Parse(td.Get(asset["__id"].ToString()));
                        if (previewExists)
                        {
                            asset.Add("previews", fullAsset["previews"]);
                        }
                        if (columnsDataProfilesExists)
                        {
                            asset.Add("columnsDataProfiles", fullAsset["columnsDataProfiles"]);
                        }
                        if (tableDataProfilesExist)
                        {
                            asset.Add("tableDataProfiles", fullAsset["tableDataProfiles"]);
                        }
                    }

                    StripSystemProperties(asset.Children());

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

                string id = asset["__id"].ToString();
                asset.Remove("__id");
                string[] idInfo = id.Split(new char[] { '/' });
                string newid;

                string UpdateResponse = td.Update(asset.ToString(), idInfo[0], out newid);

                if ((UpdateResponse != null) && (!string.IsNullOrEmpty(newid)))
                {
                    totalAssetsImportSucceeded++;

                    if (totalAssetsImportSucceeded % 50 == 0)
                    {
                        System.Console.WriteLine(totalAssetsImportSucceeded  + "Assets Imported Succesfully");
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

        static void StripSystemProperties(JEnumerable<JToken> children)
        {
            foreach (JToken child in children)
            {
                if (child.Type.ToString().Equals("Property"))
                {

                }
                else if (child.Type.ToString().Equals("Array"))
                {

                }
                else if (child.Type.ToString().Equals("Object"))
                {
                    JObject obj = (JObject)child;
                    obj.Remove("__permissions");
                    obj.Remove("__effectiveRights");
                    bool retVal = obj.Remove("__roles");
                    obj.Remove("__type");
                    obj.Remove("__id");

                    //If there was a roles object then replace it.
                    if (retVal)
                    {
                        //Set contributor equal to "Everyone" on all nodes. This allows them to be updated by others later.  Ideally we would preserve the contributor but that requires
                        //a special platform to enable it.
                        JToken contributor = JObject.Parse("{'role': 'Contributor','members': [{'objectId': '00000000-0000-0000-0000-000000000201'}]}");
                        JArray roles = new JArray();
                        roles.Add(contributor);
                        obj.Add("__roles", roles);
                    }

                    if ((obj["__creatorId"] != null) && (obj["__creatorId"].ToString().Contains("@")))
                    {
                        obj["__creatorId"] = "imported_" + obj["__creatorId"].ToString();
                    }
                }
                else
                {
                    return;
                }

                StripSystemProperties(child.Children());
            }
        }
    }

    public class AzureDataCatalog
    {
        private readonly string ClientId;
        private readonly Uri RedirectUri;
        private readonly string CatalogName;

        private readonly AuthenticationResult auth;
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

            auth = AuthContext.AcquireToken("https://datacatalog.azure.com", ClientId, RedirectUri, PromptBehavior.Always);
        }

        public string Get(string id)
        {
        
            var fullUri = string.Format("https://api.azuredatacatalog.com/catalogs/{0}/views/{1}?api-version=2015-07.1.0-Preview", CatalogName, id);

            string requestId;
            HttpWebRequest request = CreateHttpRequest(fullUri, "GET", out requestId);

            try
            {
                string s = GetPayload(ref request);
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
                                Console.WriteLine("Failed Get of asset: " + id);
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
            var fullUri = string.Format("https://api.azuredatacatalog.com/catalogs/{0}/search/search?searchTerms={1}&count={2}&startPage={3},&api-version=2015-06.0.1-Preview", CatalogName, searchTerm, count, startPage);

            string requestId;
            HttpWebRequest request = CreateHttpRequest(fullUri, "GET", out requestId);

            try
            {
                string s = GetPayload(ref request);
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

            var fullUri = string.Format("https://api.azuredatacatalog.com/catalogs/{0}/views/{1}?api-version=2015-07.1.0-Preview", CatalogName, viewType);

            string requestId;
            var request = CreateHttpRequest(fullUri, "POST", out requestId);

            try
            {
                request.ContentType = "application/json";

                byte[] postData = System.Text.Encoding.UTF8.GetBytes(postPayload);
                request.ContentLength = postData.Length;

                Stream requestStream = request.GetRequestStream();

                requestStream.Write(postData, 0, postData.Length);

                var response = (HttpWebResponse)request.GetResponse();
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
                                Console.WriteLine("Failed Update of asset: " + postPayload.Substring(0,50));
                                Console.WriteLine(reader.ReadToEnd());
                            }
                        }
                    }
                }
                id = null;
                return null;
            }
        }

        private HttpWebRequest CreateHttpRequest(string url, string method, out string requestId)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;

            string authHeader = auth.CreateAuthorizationHeader();
            request.Headers.Add("Authorization", authHeader);
            requestId = Guid.NewGuid().ToString();
            request.Headers.Add("x-ms-client-request-id", requestId);

            return request;
        }

        private string GetPayload(ref HttpWebRequest request)
        {
            string result = String.Empty;
            var response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode != HttpStatusCode.OK)
                throw new ApplicationException("Request wrong");
            var stream = response.GetResponseStream();
            StreamReader reader = new StreamReader(stream);
            result = reader.ReadToEnd();
            return result;
        }

        private static SecureString ToSecureString(string str)
        {
            var result = new SecureString();
            foreach (var c in str.ToCharArray())
            {
                result.AppendChar(c);
            }
            result.MakeReadOnly();
            return result;
        }

    }
}
