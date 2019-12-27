using Dcomms.UserApp.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Dcomms.UserApp
{
    public class UserAppConfiguration
    {
        public IPEndPoint[] EpEndPoints = new IPEndPoint[0];
        public string EpEndPointsString
        {
            get
            {
                if (EpEndPoints == null) return "";
                return String.Join(";", EpEndPoints.Select(x => x.ToString()));
            }
            set
            {
                if (String.IsNullOrEmpty(value)) EpEndPoints = null;
                else EpEndPoints = (from valueStr in value.Split(';')
                                          let pos = valueStr.IndexOf(':')
                                          where pos != -1
                                          select new IPEndPoint(
                                              IPAddress.Parse(valueStr.Substring(0, pos)),
                                              int.Parse(valueStr.Substring(pos + 1))
                                              )
                        ).ToArray();
            }
        }
        public string DatabaseBasePathNullable; // default: Assembly.GetExecutingAssembly().Location
        public IDatabaseKeyProvider DatabaseKeyProvider;

        public static UserAppConfiguration Default => new UserAppConfiguration
        { 
            EpEndPointsString = "192.99.160.225:12000;195.154.173.208:12000;5.135.179.50:12000",
            DatabaseKeyProvider = new EmptyDatabaseKeyProvider()
        };
    }
}
