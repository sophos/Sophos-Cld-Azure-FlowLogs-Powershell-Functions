using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Collections;

class VNETFlowLogTuple
{
    float schemaVersion;

    string startTime;
    string sourceAddress;
    string destinationAddress;
    string sourcePort;
    string destinationPort;
    string transportProtocol;
    string deviceDirection;

    // version 2 tuple properties
    string flowState;
    string packetsStoD;
    string bytesStoD;
    string packetsDtoS;
    string bytesDtoS;

    public VNETFlowLogTuple(string tuple, float version)
    {
        schemaVersion = version;

        char[] sep = new char[] { ',' };
        string[] parts = tuple.Split(sep);
        startTime = parts[0];
        sourceAddress = parts[1];
        destinationAddress = parts[2];
        sourcePort = parts[3];
        destinationPort = parts[4];
        transportProtocol = parts[5];
        deviceDirection = parts[6];

        if (version >= 2.0)
        {
             flowState = parts[7];
             packetsStoD = parts[9];
             bytesStoD = parts[10];
             packetsDtoS = parts[11];
             bytesDtoS = parts[12];
        }
    }

    public string GetDirection
    {
        get { return deviceDirection; }
    }

    public override string ToString()
    {
        var temp = new StringBuilder();
        temp.Append("rt=").Append((Convert.ToUInt64(startTime) * 1000).ToString());
        temp.Append(" src=").Append(sourceAddress);
        temp.Append(" dst=").Append(destinationAddress);
        temp.Append(" spt=").Append(sourcePort);
        temp.Append(" dpt=").Append(destinationPort);
        temp.Append(" proto=").Append((transportProtocol == "U" ? "UDP" : "TCP"));
        temp.Append(" deviceDirection=").Append((deviceDirection == "I" ? "0" : "1"));

        if (schemaVersion >= 2.0)
        {
            // add fields from version 2 schema
            temp.Append(" cs2=").Append(flowState);
            temp.Append(" cs2Label=FlowState");

            temp.Append(" cn1=").Append(packetsStoD);
            temp.Append(" cn1Label=PacketsStoD");
            temp.Append(" cn2=").Append(packetsDtoS);
            temp.Append(" cn2Label=PacketsDtoS");

            if (deviceDirection == "I")
            {
                temp.Append(" bytesIn={0}").Append(bytesStoD);
                temp.Append(" bytesOut={0}").Append(bytesDtoS);
            }
            else
            {
                temp.Append(" bytesIn={0}").Append(bytesDtoS);
                temp.Append(" bytesOut={0}").Append(bytesStoD);
            }
        }

        return temp.ToString();
    }

    public string JsonSubString()
    {
        var sb = new StringBuilder();
        sb.Append(",\"rt\":\"").Append((Convert.ToUInt64(startTime) * 1000).ToString()).Append("\"");
        sb.Append(",\"src\":\"").Append(sourceAddress).Append("\"");
        sb.Append(",\"dst\":\"").Append(destinationAddress).Append("\"");
        sb.Append(",\"spt\":\"").Append(sourcePort).Append("\"");
        sb.Append(",\"dpt\":\"").Append(destinationPort).Append("\"");
        sb.Append(",\"proto\":\"").Append((transportProtocol == "U" ? "UDP" : "TCP")).Append("\"");
        sb.Append(",\"deviceDirection\":\"").Append((deviceDirection == "I" ? "0" : "1")).Append("\"");

        return sb.ToString();
    }
}

class VNETFlowLogsInnerFlowGroups
{
    public string rule { get; set; }
    public string[] flowTuples { get; set; }
}

class VNETFlowLogsOuterFlows
{
    public string aclID { get; set; }
    public VNETFlowLogsInnerFlowGroups[] flowGroups { get; set; }
}

class VNETFlowRecords
{
    public VNETFlowLogsOuterFlows[] flows { get; set; }
}

class VNETFlowLogRecord
{
    public string time { get; set; }
    public float flowLogVersion { get; set; }
    public string flowLogGUID { get; set; }
    public string macAddress { get; set; }
    public string category { get; set; }
    public string flowLogResourceID { get; set; }
    public string targetResourceID { get; set; }
    public string operationName { get; set; }

    public VNETFlowRecords flowRecords { get; set; }

    public string MakeDeviceExternalID()
    {
        var patternSubscriptionId = "SUBSCRIPTIONS\\/(.*?)\\/";
        var patternResourceGroup = "SUBSCRIPTIONS\\/(?:.*?)\\/RESOURCEGROUPS\\/(.*?)\\/";
        var patternResourceName = "PROVIDERS\\/(?:.*?\\/.*?\\/)(.*?)(?:\\/|$)";

        Match m = Regex.Match(flowLogResourceID, patternSubscriptionId);
        var subscriptionID = m.Groups[1].Value;

        m = Regex.Match(flowLogResourceID, patternResourceGroup);
        var resourceGroup = m.Groups[1].Value;

        m = Regex.Match(flowLogResourceID, patternResourceName);
        var resourceName = m.Groups[1].Value;

        return subscriptionID + "/" + resourceGroup + "/" + resourceName;
    }

    public string MakeCEFTime()
    {
        // sample input: "2017-08-09T00:13:25.4850000Z"
        // sample output: Aug 09 00:13:25 host CEF:0

        CultureInfo culture = new CultureInfo("en-US");
        DateTime tempDate = Convert.ToDateTime(time, culture);
        string newTime = tempDate.ToString("MMM dd HH:mm:ss");

        return newTime + " host CEF:0";
    }

    public override string ToString()
    {
        string temp = MakeDeviceExternalID();
        return temp;
    }

    public string MakeMAC()
    {
        var temp = new StringBuilder();
        temp.Append(macAddress.Substring(0, 2)).Append(":");
        temp.Append(macAddress.Substring(2, 2)).Append(":");
        temp.Append(macAddress.Substring(4, 2)).Append(":");
        temp.Append(macAddress.Substring(6, 2)).Append(":");
        temp.Append(macAddress.Substring(8, 2)).Append(":");
        temp.Append(macAddress.Substring(10, 2));

        return temp.ToString();
    }
}

class VNETFlowLogRecords
{
    public VNETFlowLogRecord[] records { get; set; }
    public String uuid {get; set;}
}

class Token
{
    public string access_token { get; set; }
    public string expires_on { get; set; }
    public string resource { get; set; }
    public string token_type { get; set; }
}

class NWApiResult
{
    public NetworkWatcher[] value { get; set; }
}

class NetworkWatcher
{
    public string name { get; set; }
    public string location { get; set; }
}

class NSGApiResult
{
    public NetworkSecurityGroup[] value { get; set; }
}

class NetworkSecurityGroup
{
    public string name { get; set; }
    public string location { get; set; }
    public string id { get; set; }
}

class ActivityLogsRecords
{
    public ActivityLogsRecord[] records { get; set; }
    public String uuid {get; set;}
}
class ActivityLogsRecord
{
    public string time {get; set;}
    public string resourceId {get; set;}
    public string operationName {get; set;}
    public string category {get; set;}
    public string resultType {get; set;}
    public string resultSignature {get; set;}
    public string durationMs {get; set;}
    public string callerIpAddress {get; set;}
    public string correlationId {get; set;}
    public ActivityIdentity identity {get; set;}
    public string level {get; set;}
    public string location {get; set;}
    public ActivityProperties properties {get; set;}
    public string status {get; set;}
    public string subStatus {get; set;}
    public string eventTimestamp {get; set;}
    public string httpRequest {get; set;}
    public string description {get; set;}
    public string caller {get; set;}
}
class ActivityIdentity
{
    public ActivityAuth authorization {get; set;}
    public ActivityClaim claims {get; set;}
}
class ActivityAuth
{
    public string scope {get; set;}
    public string action {get; set;}
    public ActivityEvidence evidence {get; set;}
}
class ActivityProperties
{
    public string statusCode {get; set;}
    public string serviceRequestId {get; set;}
}
class ActivityEvidence
{
    public string role {get; set;}
    public string roleAssignmentScope {get; set;}
    public string roleAssignmentId {get; set;}
    public string roleDefinitionId {get; set;}
    public string principalId {get; set;}
    public string principalType {get; set;}
}
class ActivityClaim{
    [JsonProperty(PropertyName = "aud")]
    public string aud {get; set;}
    [JsonProperty(PropertyName = "iss")]
    public string iss {get; set;}
    [JsonProperty(PropertyName = "iat")]
    public string iat {get; set;}
    [JsonProperty(PropertyName = "nbf")]
    public string nbf {get; set;}
    [JsonProperty(PropertyName = "exp")]
    public string exp {get; set;}
    [JsonProperty(PropertyName = "aio")]
    public string aio {get; set;}
    [JsonProperty(PropertyName = "appid")]
    public string appid {get; set;}
    [JsonProperty(PropertyName = "appidacr")]
    public string appidacr {get; set;}
    [JsonProperty(PropertyName = "groups")]
    public string groups {get; set;}
    [JsonProperty(PropertyName = "ipaddr")]
    public string ipaddr {get; set;}
    [JsonProperty(PropertyName = "name")]
    public string name {get; set;}
    [JsonProperty(PropertyName = "puid")]
    public string puid {get; set;}
    [JsonProperty(PropertyName = "uti")]
    public string uti {get; set;}
    [JsonProperty(PropertyName = "ver")]
    public string ver {get; set;}
    [JsonProperty(PropertyName = "wids")]
    public string wids {get; set;}
    [JsonProperty(PropertyName = "http://schemas.microsoft.com/identity/claims/identityprovider")]
    public string identityprovider { get; set; }
    [JsonProperty(PropertyName = "http://schemas.microsoft.com/identity/claims/objectidentifier")]
    public string objectidentifier { get; set; }
    [JsonProperty(PropertyName = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")]
    public string nameidentifier { get; set; }
    [JsonProperty(PropertyName = "http://schemas.microsoft.com/claims/authnmethodsreferences")]
    public string authnmethodsreferences { get; set; }
    [JsonProperty(PropertyName = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")]
    public string upn { get; set; }
    [JsonProperty(PropertyName = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname")]
    public string givenname { get; set; }
    [JsonProperty(PropertyName = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname")]
    public string surname { get; set; }
    [JsonProperty(PropertyName = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")]
    public string claimName { get; set; }
    [JsonProperty(PropertyName = "http://schemas.microsoft.com/identity/claims/scope")]
    public string scope { get; set; }
    [JsonProperty(PropertyName = "http://schemas.microsoft.com/claims/authnclassreference")]
    public string authnclassreference { get; set; }

}
