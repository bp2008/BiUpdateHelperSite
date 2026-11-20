using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("BiUpdateHelperSite")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("BiUpdateHelperSite")]
[assembly: AssemblyCopyright("Copyright ©  2020")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The Windows build targets a Windows-specific framework, and BPUtil's Windows build is annotated
// Windows-only accordingly.  The SDK normally emits this attribute itself for a "-windows" target
// framework, but only when it generates the assembly attributes, which this project does not do.
#if !LINUX
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows7.0")]
#endif

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("04a71297-2bd6-457c-b945-9ad0d6ef106c")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.8.0.0")]
[assembly: AssemblyFileVersion("1.8.0.0")]
