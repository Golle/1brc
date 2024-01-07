
&dotnet publish src/Challenge/Challenge.csproj --self-contained -c Release -r win-x64 -p:PublishAOT=true -p:DebugSymbols=false -p:DebugType=None -o release/ -p:StripSymbols=true

# &dotnet publish src/Challenge/Challenge.csproj --self-contained -c Release -r win-x64 -p:PublishSingleFile=true -p:DebugSymbols=true -o release/ -p:StripSymbols=false
&release/Challenge.exe