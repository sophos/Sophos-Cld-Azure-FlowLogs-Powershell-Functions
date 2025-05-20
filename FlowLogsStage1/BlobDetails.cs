using System;

namespace NwNsgProject
{
    public class BlobDetails
    {
        //const string macPath = "hybridflowlogtest-networksecuritygroupflowevent/resourceId=   0, 1
        //SUBSCRIPTIONS/{subId} 2, 3
        //RESOURCEGROUPS/{resourceGroup} 4, 5
        //PROVIDERS/MICROSOFT.NETWORK 6, 7
        //NETWORKSECURITYGROUPS/{nsgName}  8, 9
        //y={blobYear}  10
        //m={blobMonth} 11
        //d={blobDay} 12
        //h={blobHour} 13
        //m={blobMinute} 14
        //macAddress={mac} 15
        //PT1H.json";
        public string SubscriptionId { get; set; }
        public string ResourceGroupName { get; set; }
        public string FlowLogName { get; set; }
        public string Year { get; set; }
        public string Month { get; set; }
        public string Day { get; set; }
        public string Hour { get; set; }
        public string Minute { get; set; }
        public string Mac { get; set; }

        public BlobDetails(string path)
        {
            var parts = path.Split('/');

            var SubscriptionDetail = parts[2].Split('_');
            SubscriptionId = SubscriptionDetail[0];
            ResourceGroupName = SubscriptionDetail[1];
            FlowLogName = parts[3];
            Year = parts[4].Split('=')[1];
            Month = parts[5].Split('=')[1];
            Day = parts[6].Split('=')[1];
            Hour = parts[7].Split('=')[1];
            Minute = parts[8].Split('=')[1];
            Mac = parts[9].Split('=')[1];
        }

        public BlobDetails(string subRgName, string nsgFlowLogName, string year, string month, string day, string hour, string minute, string mac)
        {
            var subsRgName = subRgName.Split('_');
            SubscriptionId = subsRgName[0];
            ResourceGroupName = subsRgName[1];
            FlowLogName = nsgFlowLogName;
            Year = year;
            Month = month;
            Day = day;
            Hour = hour;
            Minute = minute;
            Mac = mac;
        }

        public BlobDetails(string subRgName, string nsgFlowLogName, string year, string month, string day, string hour, string minute)
        {
            var subsRgName = subRgName.Split('_');
            SubscriptionId = subsRgName[0];
            ResourceGroupName = subsRgName[1];
            FlowLogName = nsgFlowLogName;
            Year = year;
            Month = month;
            Day = day;
            Hour = hour;
            Minute = minute;
            Mac = "none";
        }

        public string GetPartitionKey()
        {
            return string.Format("{0}_{1}_{2}_{3}", SubscriptionId.Replace("-", "_"), ResourceGroupName, FlowLogName, Mac);
        }

        public string GetRowKey()
        {
            return string.Format("{0}_{1}_{2}_{3}_{4}", Year, Month, Day, Hour, Minute);
        }
    }


    public class BlobDetailsActivity
    {
        //const string macPath = "hybridflowlogtest-networksecuritygroupflowevent/resourceId=   0, 1
        //SUBSCRIPTIONS/{subId} 2, 3
        //RESOURCEGROUPS/{resourceGroup} 4, 5
        //PROVIDERS/MICROSOFT.NETWORK 6, 7
        //NETWORKSECURITYGROUPS/{nsgName}  8, 9
        //y={blobYear}  10
        //m={blobMonth} 11
        //d={blobDay} 12
        //h={blobHour} 13
        //m={blobMinute} 14
        //macAddress={mac} 15
        //PT1H.json";
        public string SubscriptionId { get; set; }
        public string Year { get; set; }
        public string Month { get; set; }
        public string Day { get; set; }
        public string Hour { get; set; }
        public string Minute { get; set; }


        public BlobDetailsActivity(string subscriptionId, string year, string month, string day, string hour, string minute)
        {
            SubscriptionId = subscriptionId;
            Year = year;
            Month = month;
            Day = day;
            Hour = hour;
            Minute = minute;
        }

        public string GetPartitionKey()
        {
            return string.Format("{0}", SubscriptionId.Replace("-", "_"));
        }

        public string GetRowKey()
        {
            return string.Format("{0}_{1}_{2}_{3}_{4}", Year, Month, Day, Hour, Minute);
        }
    }
}