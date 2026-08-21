@echo off
rem Publish: self-contained folder build (fallback, §9.1)
rem Use this when a site blocks the single .exe despite the mitigations.
rem Output: Src\App\bin\publish\PortableFolder\
dotnet publish "Src\App\HardwareAuditToolkit.App.csproj" -c Release -p:PublishProfile=PortableFolder
