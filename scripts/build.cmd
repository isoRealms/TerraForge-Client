dotnet build "../src/TerraForge.csproj" -c Release
dotnet publish "../src/TerraForge.csproj" -c Release /p:DefineConstants="STANDARD_BUILD" -p:IS_DEV_BUILD=true