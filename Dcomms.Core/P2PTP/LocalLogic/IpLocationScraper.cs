using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.P2PTP.LocalLogic
{
    internal class IpLocationScraper
    {
        readonly LocalPeer _localPeer;
        readonly string _localPublicIp;
        /// <summary>
        /// begins scraping asynchronously
        /// </summary>
        public IpLocationScraper(string localPublicIp, LocalPeer localPeer)
        {
            _localPeer = localPeer;
            _localPublicIp = localPublicIp;
#if DEBUG
            if (_localPublicIp == "127.0.0.1") _localPublicIp = "8.8.8.8";
#endif
            try
            {
                SendHttpRequestsAsync();
            }
            catch (Exception exc)
            {
                localPeer.HandleException(LogModules.IpLocationScraper, exc);
            }
        }
        async Task SendHttpRequestsAsync()
        {
            var ipapicomR = await SendHttpRequestAsync("http://ip-api.com/json/" + _localPublicIp);
            if (ipapicomR != null && TryParseJsonAttr(ipapicomR, "status") == "success")
            {
                if (TryParseJsonAttr(ipapicomR, "countryCode", out var countryCode) &&
                    TryParseJsonAttr(ipapicomR, "country", out var country) &&
                    TryParseJsonAttr(ipapicomR, "city", out var city) &&
                    TryParseJsonAttr_Double(ipapicomR, "lat", out var latitude) &&
                    TryParseJsonAttr_Double(ipapicomR, "lon", out var longitude)
                    )
                {
                    IpLocationData = new IpLocationData
                    {
                        CountryCode = countryCode,
                        Country = country,
                        City = city,
                        Latitude = latitude,
                        Longitude = longitude
                    };
                    if (TryParseJsonAttr(ipapicomR, "region", out var stateCode)) IpLocationData.StateCode = stateCode;
                    if (TryParseJsonAttr(ipapicomR, "regionName", out var state)) IpLocationData.State = state;
                    if (TryParseJsonAttr(ipapicomR, "org", out var org)) IpLocationData.Organization_ISP = org;
                    if (TryParseJsonAttr(ipapicomR, "zip", out var zip)) IpLocationData.ZIP = zip;
                    if (TryParseJsonAttr(ipapicomR, "as", out var as_)) IpLocationData.ASname = as_;
                }
            }

            if (IpLocationData == null)
            {
                var ipwhoisR = await SendHttpRequestAsync("http://free.ipwhois.io/json/" + _localPublicIp);
                if (ipwhoisR != null &&
                    TryParseJsonAttr(ipwhoisR, "success") == "true" &&
                    TryParseJsonAttr(ipwhoisR, "country_code", out var countryCode) &&
                    TryParseJsonAttr(ipwhoisR, "country", out var country) &&
                    TryParseJsonAttr(ipwhoisR, "city", out var city) &&
                    TryParseJsonAttr_Double(ipwhoisR, "latitude", out var latitude) &&
                    TryParseJsonAttr_Double(ipwhoisR, "longitude", out var longitude)
                    )
                {
                    IpLocationData = new IpLocationData
                    {
                        CountryCode = countryCode,
                        Country = country,
                        City = city,
                        Latitude = latitude,
                        Longitude = longitude
                    };
                    if (TryParseJsonAttr(ipwhoisR, "region", out var stateCode)) IpLocationData.State = stateCode;
                   // if (TryParseJsonAttr(ipwhoisR, "regionName", out var state)) IpLocationData.State = state;
                    if (TryParseJsonAttr(ipwhoisR, "org", out var org)) IpLocationData.Organization_ISP = org;
                    if (TryParseJsonAttr(ipwhoisR, "as", out var as_)) IpLocationData.ASname = as_;
                    if (TryParseJsonAttr(ipwhoisR, "asn", out var asn)) IpLocationData.AS = asn;
                }
            }
        }
        static string TryParseJsonAttr(string str, string name)
        {
            if (TryParseJsonAttr(str, name, out var value)) return value;
            else return null;
        }
        static bool TryParseJsonAttr(string str, string name, out string value)
        {
            value = null;
            try
            {
                var searchName = "\"" + name + "\"";
                var searchNameIndex = str.IndexOf(searchName);
                if (searchNameIndex == -1) return false;
                
                var valueStartIndex = searchNameIndex + searchName.Length;
                if (str[valueStartIndex] != ':') return false;
                valueStartIndex++;
                if (str[valueStartIndex] == ' ') valueStartIndex++;
                var quotesOpen = false;
                if (str[valueStartIndex] == '\"') { valueStartIndex++; quotesOpen = true; }

                           

                var valueEndIndex = str.IndexOf(quotesOpen ? "\"" : ",", valueStartIndex);
                if (valueEndIndex == -1) return false;

                value = str.Substring(valueStartIndex, valueEndIndex - valueStartIndex);
                return true;
            }
            catch
            {
                return false;
            }
        }
        static bool TryParseJsonAttr_Double(string str, string name, out double value)
        {
            value = 0;
            if (TryParseJsonAttr(str, name, out var valueStr))
            {
                if (double.TryParse(valueStr, NumberStyles.Float, new CultureInfo("en-US"), out value))
                    return true;
            }
            return false;
        }


        async Task<string> SendHttpRequestAsync(string url)
        {
            try
            {
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(3);
                var response = await httpClient.GetAsync(url);
                var result = await response.Content.ReadAsStringAsync();
                return result;
            }
            catch (Exception exc)
            {
                _localPeer.HandleException(LogModules.IpLocationScraper, exc, $"request to {url} failed: ");
                return null;
            }
        }


        /// <summary>
        /// is null while scraping is not ready
        /// </summary>
        public IpLocationData IpLocationData { get; private set; }
/*
http://ip-api.com/json/24.48.0.1
{"as":"AS5769 Videotron Telecom Ltee","city":"Saint-Leonard","country":"Canada","countryCode":"CA","isp":"Le Groupe Videotron Ltee",
"lat":45.5833,"lon":-73.6,"org":"Videotron Ltee","query":"24.48.0.1","region":"QC","regionName":"Quebec",
"status":"success","timezone":"America/Toronto","zip":"H1R"}
    
http://free.ipwhois.io/json/8.8.8.8
 {"ip":"8.8.8.8","success":true,"type":"IPv4","continent":"North America","continent_code":"NA","country":"United States","country_code":"US",
 "country_flag":"https:\/\/cdn.ipwhois.io\/flags\/us.svg","country_capital":"Washington","country_phone":"+1","country_neighbours":"CA,MX,CU",
 "region":"New Jersey","city":"Newark","latitude":"40.735657","longitude":"-74.1723667","asn":"AS15169","org":"Level 3 Communications",
 "isp":"Google LLC","timezone":"America\/New_York","timezone_name":"Eastern Standard Time","timezone_dstOffset":"0",
 "timezone_gmtOffset":"-18000","timezone_gmt":"GMT -5:00","currency":"US Dollar",
 "currency_code":"USD","currency_symbol":"$","currency_rates":"1","currency_plural":"US dollars"}

https://ipapi.co/8.8.8.8/json/
{
    "ip": "8.8.8.8",    "city": "Mountain View",
    "region": "California",    "region_code": "CA",
    "country": "US",    "country_name": "United States",
    "continent_code": "NA",    "in_eu": false,
    "postal": "94035",    "latitude": 37.386,    "longitude": -122.0838,
    "timezone": "America/Los_Angeles",
    "utc_offset": "-0700",    "country_calling_code": "+1",    "currency": "USD",
    "languages": "en-US,es-US,haw,fr",    "asn": "AS15169",
    "org": "Google LLC"
}

https://www.iplocate.io/api/lookup/8.8.8.8
{"ip":"8.8.8.8",
"country":"United States","country_code":"US",
"city":null,"continent":"North America","latitude":37.751,"longitude":-97.822,"
time_zone":null,"postal_code":null,"org":"Google LLC",
"asn":"AS15169","subdivision":null,"subdivision2":null}

https://api.ip.sb/geoip/185.222.222.222
{"longitude":8,"timezone":"Europe\/Vaduz","offset":3600,"asn":6233,"organization":"xTom","ip":"185.222.222.222","latitude":47,"continent_code":"EU"}


https://api.ipfind.com/?ip=8.8.8.8
{"ip_address":"8.8.8.8","country":"United States","country_code":"US","continent":"North America","continent_code":"NA",
"city":null,"county":null,"region":null,"region_code":null,"timezone":"America\/Chicago","owner":null,"longitude":-97.822,"latitude":37.751,"currency":"USD","languages":["en-US","es-US","haw","fr"],
"warning":"You are not using an IP Find API Key. You are limited to 100 requests\/day. Register for free at https:\/\/ipfind.co for higher limits"}

 */

    }
}
