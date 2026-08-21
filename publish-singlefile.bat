@echo off
rem Publish: portable, self-contained, single-file .exe (primary, §9.1)
rem Output: Src\App\bin\publish\PortableSingleFile\
dotnet publish "Src\App\HardwareAuditToolkit.App.csproj" -c Release -p:PublishProfile=PortableSingleFile
