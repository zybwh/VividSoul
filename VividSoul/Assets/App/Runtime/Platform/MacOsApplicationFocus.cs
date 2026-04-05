#nullable enable

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR

using System;
using System.Runtime.InteropServices;

namespace VividSoul.Runtime.Platform
{
    internal static class MacOsApplicationFocus
    {
        private const string LibObjC = "/usr/lib/libobjc.dylib";

        [DllImport(LibObjC)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(LibObjC, EntryPoint = "sel_registerName")]
        private static extern IntPtr SelRegisterName(string name);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector,
            [MarshalAs(UnmanagedType.I1)] bool value);

        public static void ActivateIgnoringOtherApps()
        {
            var nsApplicationClass = objc_getClass("NSApplication");
            if (nsApplicationClass == IntPtr.Zero)
            {
                return;
            }

            var sharedApplicationSel = SelRegisterName("sharedApplication");
            var nsApp = objc_msgSend_IntPtr(nsApplicationClass, sharedApplicationSel);
            if (nsApp == IntPtr.Zero)
            {
                return;
            }

            var activateSel = SelRegisterName("activateIgnoringOtherApps:");
            objc_msgSend_void_bool(nsApp, activateSel, true);
        }
    }
}

#endif
