Dcomms.org - secure telecommunication - open protocols and implementation

License: MIT

Contact: Aleshin Sergei Vladimirovich   

Website: http://dcomms.org



### Compiling under Linux OS 

1) please install .NET core 3.0 SDK: https://dotnet.microsoft.com/download/dotnet-core/3.0

2) got to directory "/StarTrinity.ContinuousSpeedTest.CLI", compile and run:  "dotnet run StarTrinity.ContinuousSpeedTest.CLI.csproj -- configuration Release"

3) go to directory "/bin/Release/netcoreapp3.0", run a compiled DLL file: "dotnet StarTrinity.ContinuousSpeedTest.CLI.dll target 3000000" where 3000000=3Mbps target continuous bandwidth

