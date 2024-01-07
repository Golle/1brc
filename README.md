## 1brc challenge

Publish and run this as NativeAOT.
```ps
# sample to build and run for windows
dotnet publish src/Challenge/Challenge.csproj --self-contained -c Release -r win-x64 -p:PublishAOT=true -p:DebugSymbols=false -p:DebugType=None -o release/ -p:StripSymbols=true

./release/Challenge.exe

```

measurement file is expected to be in `data/measurements.txt`, and the app should be run from the root of the repo.
