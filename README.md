If you have any problems/suggestions with the tutorial, please contact me:  

Aleshin Sergei Vladimirovich support@dcomms.org asv@startrinity.com

# Dcomms.org -  encrypted decentralized serverless telecommunications; continuous internet speed test

License: MIT

Website: http://dcomms.org



# Dcomms Messenger "T"

How to install: http://dcomms.org/MessengerT/Tutorial.aspx



# Continuous Speed Test

### Compiling on Linux 

1) install .NET core 3.1 SDK: https://dotnet.microsoft.com/download/dotnet-core/3.1

2) got to directory "/StarTrinity.ContinuousSpeedTest.CLI", compile and run:  "dotnet run StarTrinity.ContinuousSpeedTest.CLI.csproj -- configuration Release"

3) go to directory "/bin/Release/netcoreapp3.1", run a compiled DLL file: "dotnet StarTrinity.ContinuousSpeedTest.CLI.dll target 3000000" where 3000000=3Mbps target continuous bandwidth

