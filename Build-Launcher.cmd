@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Microsoft C# compiler not found.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:"assets\classic-skin-morph.ico" /out:RiftLegacyLauncher.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /resource:"assets\fonts\cinzel-500.ttf",Cinzel500 /resource:"assets\fonts\cinzel-700.ttf",Cinzel700 /resource:"assets\fonts\cinzel-900.ttf",Cinzel900 /resource:"assets\fonts\spectral-400.ttf",Spectral400 /resource:"assets\fonts\spectral-600.ttf",Spectral600 /resource:"assets\rift-legacy-icon.png",RiftIcon "src\Launcher.cs"
if errorlevel 1 exit /b 1
echo RiftLegacyLauncher.exe built successfully.
