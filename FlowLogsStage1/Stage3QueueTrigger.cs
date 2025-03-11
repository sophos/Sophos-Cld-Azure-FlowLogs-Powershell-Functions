using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Extensions;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace NwNsgProject
{
    public static class Stage3QueueTrigger
    {
        [FunctionName("Stage3QueueTrigger")]
        public static async Task Run(
            [QueueTrigger("stage2", Connection = "AzureWebJobsStorage")]Chunk inputChunk,
            Binder binder,
            Binder cefLogBinder,
            Binder errorRecordBinder,
            ILogger log)
        {
            try
            {
                string nsgSourceDataAccount = Util.GetEnvironmentVariable("nsgSourceDataAccount");
                if (nsgSourceDataAccount.Length == 0)
                {
                    log.LogError("Value for nsgSourceDataAccount is required.");
                    throw new ArgumentNullException("nsgSourceDataAccount", "Please supply in this setting the name of the connection string from which NSG logs should be read.");
                }

                var attributes = new Attribute[]
                {
                    new BlobAttribute(inputChunk.BlobName),
                    new StorageAccountAttribute(nsgSourceDataAccount)
                };

                string nsgMessagesString;
                try
                {
                    byte[] nsgMessages = new byte[inputChunk.Length];
                    CloudBlockBlob blob = await binder.BindAsync<CloudBlockBlob>(attributes);
                    await blob.DownloadRangeToByteArrayAsync(nsgMessages, 0, inputChunk.Start, inputChunk.Length);
                    nsgMessagesString = System.Text.Encoding.UTF8.GetString(nsgMessages);
                }
                catch (Exception ex)
                {
                    log.LogError(string.Format("Error binding blob input: {0}", ex.Message));
                    throw ex;
                }

                // skip past the leading comma
                string trimmedMessages = nsgMessagesString.Trim();
                int curlyBrace = trimmedMessages.IndexOf('{');
                string newClientContent = "{\"records\":[";
                newClientContent += trimmedMessages.Substring(curlyBrace);
                newClientContent += "]}";

                await SendMessagesDownstream(newClientContent, log);

                string logOutgoingCEF = Util.GetEnvironmentVariable("logOutgoingCEF");
                Boolean flag;
                if (Boolean.TryParse(logOutgoingCEF, out flag))
                {
                    if (flag)
                    {
                        await CEFLog(newClientContent, cefLogBinder, errorRecordBinder, log);
                    }
                }
            }
            catch (Exception e)
            {
                log.LogError(e, "Function Stage3QueueTrigger is failed to process request");
            }
        }

        public static async Task SendMessagesDownstream(string myMessages, ILogger log)
        {

            await obAvidSecure(myMessages, log);
        }

        static async Task CEFLog(string newClientContent, Binder cefLogBinder, Binder errorRecordBinder, ILogger log)
        {
            int count = 0;
            Byte[] transmission = new Byte[] { };

            foreach (var message in convertToCEF(newClientContent, errorRecordBinder, log))
            {

                try
                {
                    transmission = AppendToTransmission(transmission, message);

                    // batch up the messages
                    if (count++ == 1000)
                    {
                        Guid guid = Guid.NewGuid();
                        var attributes = new Attribute[]
                        {
                            new BlobAttribute(String.Format("ceflog/{0}", guid)),
                            new StorageAccountAttribute("cefLogAccount")
                        };

                        CloudBlockBlob blob = await cefLogBinder.BindAsync<CloudBlockBlob>(attributes);
                        await blob.UploadFromByteArrayAsync(transmission, 0, transmission.Length);

                        count = 0;
                        transmission = new Byte[] { };
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"Exception logging CEF output: {ex.Message}");
                }
            }

            if (count != 0)
            {
                Guid guid = Guid.NewGuid();
                var attributes = new Attribute[]
                {
                    new BlobAttribute(String.Format("ceflog/{0}", guid)),
                    new StorageAccountAttribute("cefLogAccount")
                };

                CloudBlockBlob blob = await cefLogBinder.BindAsync<CloudBlockBlob>(attributes);
                await blob.UploadFromByteArrayAsync(transmission, 0, transmission.Length);
            }
        }

        static System.Collections.Generic.IEnumerable<string> convertToCEF(string newClientContent, Binder errorRecordBinder, ILogger log)
        {
            // newClientContent is a json string with records

            VNETFlowLogRecords logs = JsonConvert.DeserializeObject<VNETFlowLogRecords>(newClientContent);

            string logIncomingJSON = Util.GetEnvironmentVariable("logIncomingJSON");
            Boolean flag;
            if (Boolean.TryParse(logIncomingJSON, out flag))
            {
                if (flag)
                {
                    logErrorRecord(newClientContent, errorRecordBinder, log).Wait();
                }
            }

            string cefRecordBase = "";
            foreach (var record in logs.records)
            {
                float version = record.flowLogVersion;

                cefRecordBase = record.MakeCEFTime();
                cefRecordBase += "|Microsoft.Network";
                cefRecordBase += "|NETWORKSECURITYGROUPS";
                cefRecordBase += "|" + version.ToString("0.0");
                cefRecordBase += "|" + record.category;
                cefRecordBase += "|" + record.operationName;
                cefRecordBase += "|1";  // severity is always 1
                cefRecordBase += "|deviceExternalId=" + record.MakeDeviceExternalID();

                foreach (var outerFlows in record.flowRecords.flows)
                {
                    // expectation is that there is only ever 1 item in record.flowRecords.flows
                    string cefOuterFlowRecord = cefRecordBase;
                    cefOuterFlowRecord += String.Format(" cs1={0}", outerFlows.flowGroups.rule);
                    cefOuterFlowRecord += String.Format(" cs1Label=NSGRuleName");

                    foreach (var innerFlows in outerFlows.flowGroups)
                    {
                        var cefInnerFlowRecord = cefOuterFlowRecord;

                        var firstFlowTupleEncountered = true;
                        foreach (var flowTuple in innerFlows.flowTuples)
                        {
                            var tuple = new VNETFlowLogTuple(flowTuple, version);

                            if (firstFlowTupleEncountered)
                            {
                                cefInnerFlowRecord += (tuple.GetDirection == "I" ? " dmac=" : " smac=") + record.MakeMAC();
                                firstFlowTupleEncountered = false;
                            }

                            yield return cefInnerFlowRecord + " " + tuple.ToString();
                        }
                    }
                }
            }
        }

        static async Task logErrorRecord(NSGFlowLogRecord errorRecord, Binder errorRecordBinder, ILogger log)
        {
            if (errorRecordBinder == null) { return; }

            Byte[] transmission = new Byte[] { };

            try
            {
                transmission = AppendToTransmission(transmission, errorRecord.ToString());

                Guid guid = Guid.NewGuid();
                var attributes = new Attribute[]
                {
                    new BlobAttribute(String.Format("errorrecord/{0}", guid)),
                    new StorageAccountAttribute("cefLogAccount")
                };

                CloudBlockBlob blob = await errorRecordBinder.BindAsync<CloudBlockBlob>(attributes);
                blob.UploadFromByteArray(transmission, 0, transmission.Length);

                transmission = new Byte[] { };
            }
            catch (Exception ex)
            {
                log.LogError($"Exception logging record: {ex.Message}");
            }
        }

        static async Task logErrorRecord(string errorRecord, Binder errorRecordBinder, ILogger log)
        {
            if (errorRecordBinder == null) { return; }

            Byte[] transmission = new Byte[] { };

            try
            {
                transmission = AppendToTransmission(transmission, errorRecord);

                Guid guid = Guid.NewGuid();
                var attributes = new Attribute[]
                {
                    new BlobAttribute(String.Format("errorrecord/{0}", guid)),
                    new StorageAccountAttribute("cefLogAccount")
                };

                CloudBlockBlob blob = await errorRecordBinder.BindAsync<CloudBlockBlob>(attributes);
                blob.UploadFromByteArray(transmission, 0, transmission.Length);

                transmission = new Byte[] { };
            }
            catch (Exception ex)
            {
                log.LogError($"Exception logging record: {ex.Message}");
            }
        }

        static async Task obArcsight(string newClientContent, ILogger log)
        {
            //
            // newClientContent looks like this:
            //
            // {
            //   "records":[
            //     {...},
            //     {...}
            //     ...
            //   ]
            // }
            //
            string arcsightAddress = Util.GetEnvironmentVariable("arcsightAddress");
            string arcsightPort = Util.GetEnvironmentVariable("arcsightPort");

            if (arcsightAddress.Length == 0 || arcsightPort.Length == 0)
            {
                log.LogError("Values for arcsightAddress and arcsightPort are required.");
                return;
            }

            TcpClient client = new TcpClient(arcsightAddress, Convert.ToInt32(arcsightPort));
            NetworkStream stream = client.GetStream();

            int count = 0;
            Byte[] transmission = new Byte[] { };
            foreach (var message in convertToCEF(newClientContent, null, log))
            {

                try
                {
                    transmission = AppendToTransmission(transmission, message);

                    // batch up the messages
                    if (count++ == 1000)
                    {
                        await stream.WriteAsync(transmission, 0, transmission.Length);
                        count = 0;
                        transmission = new Byte[] { };
                    }
                }
                catch (Exception ex)
                {
                    log.LogError($"Exception sending to ArcSight: {ex.Message}");
                }
            }
            if (count > 0)
            {
                try
                {
                    await stream.WriteAsync(transmission, 0, transmission.Length);
                }
                catch (Exception ex)
                {
                    log.LogError($"Exception sending to ArcSight: {ex.Message}");
                }
            }
            await stream.FlushAsync();
        }

        public static bool ValidateMyCert(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslErr)
        {
            var splunkCertThumbprint = Util.GetEnvironmentVariable("splunkCertThumbprint");

            // if user has not configured a cert, anything goes
            if (splunkCertThumbprint == "")
                return true;

            // if user has configured a cert, must match
            var thumbprint = cert.GetCertHashString();
            if (thumbprint == splunkCertThumbprint)
                return true;

            return false;
        }

        static async Task obAvidSecure(string newClientContent, ILogger log)
        {

            string avidAddress = Util.GetEnvironmentVariable("avidAddress");
            // string splunkToken = Util.GetEnvironmentVariable("splunkToken");

            if (avidAddress.Length == 0)
            {
                log.LogError("Values for splunkAddress and splunkToken are required.");
                return;
            }


            string customerid = Util.GetEnvironmentVariable("customerId");
            VNETFlowLogRecords logs = JsonConvert.DeserializeObject<VNETFlowLogRecords>(newClientContent);
            logs.uuid = customerid;
            string jsonString = JsonConvert.SerializeObject(logs);

            var client = new SingleHttpClientInstance();
            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, avidAddress);
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                // req.Headers.Add("Authorization", "Splunk " + splunkToken);
                req.Content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await SingleHttpClientInstance.SendToSplunk(req);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new System.Net.Http.HttpRequestException($"StatusCode from Splunk: {response.StatusCode}, and reason: {response.ReasonPhrase}");
                }
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                throw new System.Net.Http.HttpRequestException("Sending to Splunk. Is Splunk service running?", e);
            }
            catch (Exception f)
            {
                throw new System.Exception("Sending to Splunk. Unplanned exception.", f);
            }

        }

        static async Task obSplunk(string newClientContent, ILogger log)
        {


            string splunkAddress = Util.GetEnvironmentVariable("splunkAddress");
            string splunkToken = Util.GetEnvironmentVariable("splunkToken");

            if (splunkAddress.Length == 0 || splunkToken.Length == 0)
            {
                log.LogError("Values for splunkAddress and splunkToken are required.");
                return;
            }

            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback += new RemoteCertificateValidationCallback(ValidateMyCert);

            var transmission = new StringBuilder();
            foreach (var message in convertToSplunk(newClientContent, null, log))
            {
                transmission.Append(GetSplunkEventFromMessage(message));
            }

            var client = new SingleHttpClientInstance();
            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, splunkAddress);
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Headers.Add("Authorization", "Splunk " + splunkToken);
                req.Content = new StringContent(transmission.ToString(), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await SingleHttpClientInstance.SendToSplunk(req);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new System.Net.Http.HttpRequestException($"StatusCode from Splunk: {response.StatusCode}, and reason: {response.ReasonPhrase}");
                }
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                throw new System.Net.Http.HttpRequestException("Sending to Splunk. Is Splunk service running?", e);
            }
            catch (Exception f)
            {
                throw new System.Exception("Sending to Splunk. Unplanned exception.", f);
            }

        }

        static System.Collections.Generic.IEnumerable<string> convertToSplunk(string newClientContent, Binder errorRecordBinder, ILogger log)
        {


            VNETFlowLogRecords logs = JsonConvert.DeserializeObject<VNETFlowLogRecords>(newClientContent);

            string logIncomingJSON = Util.GetEnvironmentVariable("logIncomingJSON");
            Boolean flag;
            if (Boolean.TryParse(logIncomingJSON, out flag))
            {
                if (flag)
                {
                    logErrorRecord(newClientContent, errorRecordBinder, log).Wait();
                }
            }

            var sbBase = new StringBuilder();
            foreach (var record in logs.records)
            {
                sbBase.Clear();
                sbBase.Append("{");
                sbBase.Append("\"time\":\"").Append(record.time).Append("\"");
                sbBase.Append(",\"category\":\"").Append(record.category).Append("\"");
                sbBase.Append(",\"operationName\":\"").Append(record.operationName).Append("\"");
                sbBase.Append(",\"version\":\"").Append(record.flowLogVersion.ToString("0.0")).Append("\"");
                sbBase.Append(",\"deviceExtId\":\"").Append(record.MakeDeviceExternalID()).Append("\"");

                int count = 1;
                var sbOuterFlowRecord = new StringBuilder();
                foreach (var outerFlows in record.flowRecords.flows)
                {
                    sbOuterFlowRecord.Clear();
                    sbOuterFlowRecord.Append(sbBase.ToString());
                    sbOuterFlowRecord.Append(",\"flowOrder\":\"").Append(count).Append("\"");
                    sbOuterFlowRecord.Append(",\"nsgRuleName\":\"").Append(outerFlows.flowGroups.rule).Append("\"");

                    var sbInnerFlowRecord = new StringBuilder();
                    foreach (var innerFlows in outerFlows.flowGroups)
                    {
                        sbInnerFlowRecord.Clear();
                        sbInnerFlowRecord.Append(sbOuterFlowRecord.ToString());

                        var firstFlowTupleEncountered = true;
                        foreach (var flowTuple in innerFlows.flowTuples)
                        {
                            float version = 2.0F;
                            var tuple = new VNETFlowLogTuple(flowTuple, version);

                            if (firstFlowTupleEncountered)
                            {
                                sbInnerFlowRecord.Append((tuple.GetDirection == "I" ? ",\"dmac\":\"" : ",\"smac\":\"")).Append(record.MakeMAC()).Append("\"");
                                firstFlowTupleEncountered = false;
                            }

                            yield return sbInnerFlowRecord.Append(tuple.JsonSubString()).ToString() + "}";
                        }

                    }
                }
            }
        }

        static StringBuilder sb = new StringBuilder();
        static string GetSplunkEventFromMessage(string message)
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(message);

            sb.Clear();
            sb.Append("{");
            sb.Append("\"sourcetype\": \"").Append("nsgFlowLog").Append("\",");
            sb.Append("\"event\": ").Append(json);
            sb.Append("}");
            return sb.ToString();

        }

        static Byte[] AppendToTransmission(Byte[] existingMessages, string appendMessage)
        {
            Byte[] appendMessageBytes = Encoding.ASCII.GetBytes(appendMessage);
            Byte[] crlf = new Byte[] { 0x0D, 0x0A };

            Byte[] newMessages = new Byte[existingMessages.Length + appendMessage.Length + 2];

            existingMessages.CopyTo(newMessages, 0);
            appendMessageBytes.CopyTo(newMessages, existingMessages.Length);
            crlf.CopyTo(newMessages, existingMessages.Length + appendMessageBytes.Length);

            return newMessages;
        }

        public class SingleHttpClientInstance
        {
            private static readonly HttpClient HttpClient;

            static SingleHttpClientInstance()
            {
                HttpClient = new HttpClient();
                HttpClient.Timeout = new TimeSpan(0, 1, 0);
            }

            public static async Task<HttpResponseMessage> SendToLogstash(HttpRequestMessage req, ILogger log)
            {
                HttpResponseMessage response = null;
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);
                try
                {
                    response = await httpClient.SendAsync(req);
                }
                catch (AggregateException ex)
                {
                    log.LogError("Got AggregateException.");
                    throw ex;
                }
                catch (TaskCanceledException ex)
                {
                    log.LogError("Got TaskCanceledException.");
                    throw ex;
                }
                catch (Exception ex)
                {
                    log.LogError("Got other exception.");
                    throw ex;
                }
                return response;
            }

            public static async Task<HttpResponseMessage> SendToSplunk(HttpRequestMessage req)
            {
                HttpResponseMessage response = await HttpClient.SendAsync(req);
                return response;
            }

        }

        static async Task obLogstash(string newClientContent, ILogger log)
        {
            string logstashAddress = Util.GetEnvironmentVariable("logstashAddress");
            string logstashHttpUser = Util.GetEnvironmentVariable("logstashHttpUser");
            string logstashHttpPwd = Util.GetEnvironmentVariable("logstashHttpPwd");

            if (logstashAddress.Length == 0 || logstashHttpUser.Length == 0 || logstashHttpPwd.Length == 0)
            {
                log.LogError("Values for logstashAddress, logstashHttpUser and logstashHttpPwd are required.");
                return;
            }

            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback =
            new System.Net.Security.RemoteCertificateValidationCallback(
                delegate { return true; });

            // log.LogInformation($"newClientContent: {newClientContent}");

            var client = new SingleHttpClientInstance();
            var creds = Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(string.Format("{0}:{1}", logstashHttpUser, logstashHttpPwd)));
            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, logstashAddress);
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Headers.Add("Authorization", "Basic " + creds);
                req.Content = new StringContent(newClientContent, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await SingleHttpClientInstance.SendToLogstash(req, log);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    log.LogError($"StatusCode from Logstash: {response.StatusCode}, and reason: {response.ReasonPhrase}");
                }
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                string msg = e.Message;
                if (e.InnerException != null)
                {
                    msg += " *** " + e.InnerException.Message;
                }
                log.LogError($"HttpRequestException Error: \"{msg}\" was caught while sending to Logstash.");
                throw e;
            }
            catch (Exception f)
            {
                string msg = f.Message;
                if (f.InnerException != null)
                {
                    msg += " *** " + f.InnerException.Message;
                }
                log.LogError($"Unknown error caught while sending to Logstash: \"{f.ToString()}\"");
                throw f;
            }
        }
    }
}
