@echo off
rem ============================================================================
rem  HardwareAuditToolkit - single-file launcher (fixes the first-run block)
rem
rem  Why this exists:
rem    The .NET single-file self-extractor reads DOTNET_BUNDLE_EXTRACT_BASE_DIR
rem    in the native host BEFORE any managed code runs. Without it, the first
rem    launch unpacks native DLLs into %TEMP%, which enterprise EDR/AppLocker
rem    flags as dropper-like -> "Windows cannot access the specified device
rem    path or file" even as admin.
rem
rem  Fix:
rem    Set the extraction base dir to THIS folder (where the exe lives) so the
rem    first and every launch extracts next to the exe instead of %TEMP%.
rem    The runtime still keeps it in a .net\<app>\<version> subfolder here.
rem
rem  Usage:  double-click this file, or run it from the command prompt.
rem ============================================================================
setlocal
set "DOTNET_BUNDLE_EXTRACT_BASE_DIR=%~dp0"
"%~dp0HardwareAuditToolkit.exe" %*
endlocal
