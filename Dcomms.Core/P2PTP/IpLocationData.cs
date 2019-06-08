using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.P2PTP
{
    /// <summary>
    /// encoded in hello packet
    /// </summary>
    class IpLocationData
    {
        byte Flags;
        string IP;
        double Longitude, Latitude;
        string Country, City, State, ZIP;
        string Organization_ISP, AS, ASname;
        bool? mobile, proxy;

        /*
https://ipgeolocation.io/         
"ip": 87.117.173.127 
"country_name": Russia 
"state_prov": Volga Federal District 
"city": Kazan 
"latitude": 55.84300 
"longitude": 49.03850 
"time_zone": Europe/Moscow 
"gmt_offset": 3 
"isp": Teleset Company. City of Kazan. 

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
