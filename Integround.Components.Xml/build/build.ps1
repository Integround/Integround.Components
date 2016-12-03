# Set up variables:
$Version = "1.6.0"
$ProductName = "Integround.Components.Xml"
$MsbuildPath = "C:\Program Files (x86)\MSBuild\14.0\Bin"
$NugetPath = "..\packages\NuGet.CommandLine.3.4.3\tools"

# Generate version numbers:
$Revision = [Math]::Floor((New-TimeSpan -Start (Get-Date).Date -End (Get-Date)).TotalSeconds / 2)
$FullVersion = "$Version.$Revision"

# Set the assembly version numbers:
$files = "..\Integround.Components.Xml.FlatFile\Properties\AssemblyInfo.cs", "..\Integround.Components.Xml.Xslt\Properties\AssemblyInfo.cs"
ForEach ($file In $files) 
{
	(Get-Content ($file)) -replace 'Version\(".*"\)', "Version(`"$FullVersion`")" | Out-File $file
}

# Set nuget version:
$nugetConfiguration = ".\Integround.Components.Xml.nuspec"
(Get-Content $nugetConfiguration) -replace '<version>.*</version>', "<version>$FullVersion</version>" | Out-File $nugetConfiguration

# Build the solution in release mode:
& $MsbuildPath\msbuild.exe /t:Clean,Rebuild /p:Configuration=Release ..\$ProductName.sln

# Create the build folder:
$BuildPath = ".\Build-$FullVersion"
New-Item -Path $BuildPath -ItemType Directory

# Build the nuget package:
& $NugetPath\nuget.exe pack $ProductName.nuspec -o $BuildPath