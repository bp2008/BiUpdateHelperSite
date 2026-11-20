using System;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace BiUpdateHelperSite
{
	/// <summary>
	/// <para>Hands this process over to the Windows service control manager.</para>
	/// <para>This is a type of its own, rather than a few lines inside <see cref="MainStatic"/>, so that no
	/// method which runs on a non-Windows platform mentions <c>ServiceBase</c>.  The runtime loads a type,
	/// and the assembly it lives in, when a method which refers to it is first compiled; a platform which
	/// never calls into here therefore never loads System.ServiceProcess.ServiceController at all.  That
	/// matters because the portable build of that assembly, which is the one a non-Windows publish gets,
	/// throws <see cref="PlatformNotSupportedException"/> from the <c>ServiceBase</c> constructor.</para>
	/// </summary>
	[SupportedOSPlatform("windows")]
	internal static class WindowsServiceHost
	{
		/// <summary>
		/// <para>Runs the service under the Windows service control manager.  Returns when the service has
		/// stopped.</para>
		/// <para>Inlining is suppressed deliberately: were this inlined into its caller, compiling the
		/// caller would resolve the <c>ServiceBase</c> references in here, which is exactly what must not
		/// happen off Windows.</para>
		/// </summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void Run()
		{
			ServiceBase.Run(new MainSvc());
		}
	}
}
