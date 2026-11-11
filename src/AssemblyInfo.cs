using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("PxeVice")]
[assembly: AssemblyDescription("Clears leftover PXE data so a machine can PXE boot again")]
[assembly: AssemblyProduct("PxeVice")]
[assembly: AssemblyCompany("Daniel L. Baldissera")]
[assembly: AssemblyCopyright("Copyright © 2026 Daniel L. Baldissera")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyInformationalVersion("0.1.0")]
[assembly: ComVisible(false)]
// Load Windows DLLs from System32 only, never from the folder PxeVice runs from.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.5", FrameworkDisplayName = ".NET Framework 4.5")]
