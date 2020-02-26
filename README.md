If you have any problems/suggestions with the tutorial, please contact me: asv@startrinity.com asv128@mail.ru  skype asv128 linkedIN:  https://www.linkedin.com/in/sergey-aleshin-startrinity  Aleshin Sergei Vladimirovich

Dcomms.org - secure telecommunication - open protocols and implementation

License: MIT

Website: http://dcomms.org



# Dcomms Messenger "T"

### Installing from binaries on Windows

1. download binaries from http://dcomms.org/binaries/MessengerT_win.zip  and extract the ZIP archive into clean directory
2. install  ".NET core runtime" and "ASP .NET core 3.1 runtime": https://dotnet.microsoft.com/download/dotnet-core/3.1
3. run the messenger: Dcomms.MessengerT.exe
4. open browser to use the messenger: http://localhost:5050
5. create local account (your own user's account), one or multiple accounts
6. add a contact: send invitation key to a friend or receive invitation from him
7. when both you and your friend add each other into contacts, select the new contact and write messages



# Continuous Speed Test

### Compiling on Linux 

1) install .NET core 3.1 SDK: https://dotnet.microsoft.com/download/dotnet-core/3.1

2) got to directory "/StarTrinity.ContinuousSpeedTest.CLI", compile and run:  "dotnet run StarTrinity.ContinuousSpeedTest.CLI.csproj -- configuration Release"

3) go to directory "/bin/Release/netcoreapp3.1", run a compiled DLL file: "dotnet StarTrinity.ContinuousSpeedTest.CLI.dll target 3000000" where 3000000=3Mbps target continuous bandwidth

