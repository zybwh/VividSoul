#nullable enable

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR

using System;
using System.IO;
using System.Runtime.InteropServices;
using SFB;

namespace VividSoul.Runtime.Platform
{
    internal static class MacOsNativeFileDialog
    {
        private const string LibObjC = "/usr/lib/libobjc.dylib";
        private const long ModalResponseOk = 1;

        [DllImport(LibObjC)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(LibObjC, EntryPoint = "sel_registerName")]
        private static extern IntPtr SelRegisterName(string name);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr_bool(
            IntPtr receiver,
            IntPtr selector,
            IntPtr firstArgument,
            [MarshalAs(UnmanagedType.I1)] bool secondArgument);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern long objc_msgSend_long(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_void_bool(
            IntPtr receiver,
            IntPtr selector,
            [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr value);

        public static string[] OpenFilePanel(string title, string directory, ExtensionFilter[] extensions)
        {
            return OpenPanel(
                title,
                directory,
                canChooseFiles: true,
                canChooseDirectories: false,
                extensions);
        }

        public static string[] OpenFolderPanel(string title, string directory)
        {
            return OpenPanel(
                title,
                directory,
                canChooseFiles: false,
                canChooseDirectories: true,
                Array.Empty<ExtensionFilter>());
        }

        private static string[] OpenPanel(
            string title,
            string directory,
            bool canChooseFiles,
            bool canChooseDirectories,
            ExtensionFilter[] extensions)
        {
            MacOsApplicationFocus.ActivateIgnoringOtherApps();

            var openPanelClass = objc_getClass("NSOpenPanel");
            if (openPanelClass == IntPtr.Zero)
            {
                throw new InvalidOperationException("NSOpenPanel class was not found.");
            }

            var openPanel = objc_msgSend_IntPtr(openPanelClass, SelRegisterName("openPanel"));
            if (openPanel == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create NSOpenPanel.");
            }

            objc_msgSend_void_bool(openPanel, SelRegisterName("setCanChooseFiles:"), canChooseFiles);
            objc_msgSend_void_bool(openPanel, SelRegisterName("setCanChooseDirectories:"), canChooseDirectories);
            objc_msgSend_void_bool(openPanel, SelRegisterName("setAllowsMultipleSelection:"), false);
            objc_msgSend_void_bool(openPanel, SelRegisterName("setCanCreateDirectories:"), false);

            var message = CreateNSString(title);
            if (message != IntPtr.Zero)
            {
                objc_msgSend_void_IntPtr(openPanel, SelRegisterName("setMessage:"), message);
            }

            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                var directoryPath = CreateNSString(Path.GetFullPath(directory));
                if (directoryPath != IntPtr.Zero)
                {
                    var nsUrlClass = objc_getClass("NSURL");
                    var directoryUrl = objc_msgSend_IntPtr_IntPtr_bool(
                        nsUrlClass,
                        SelRegisterName("fileURLWithPath:isDirectory:"),
                        directoryPath,
                        true);
                    if (directoryUrl != IntPtr.Zero)
                    {
                        objc_msgSend_void_IntPtr(openPanel, SelRegisterName("setDirectoryURL:"), directoryUrl);
                    }
                }
            }

            var allowedExtensions = CreateExtensionArray(extensions);
            if (allowedExtensions != IntPtr.Zero)
            {
                objc_msgSend_void_IntPtr(openPanel, SelRegisterName("setAllowedFileTypes:"), allowedExtensions);
            }

            var result = objc_msgSend_long(openPanel, SelRegisterName("runModal"));
            if (result != ModalResponseOk)
            {
                return Array.Empty<string>();
            }

            var url = objc_msgSend_IntPtr(openPanel, SelRegisterName("URL"));
            if (url == IntPtr.Zero)
            {
                return Array.Empty<string>();
            }

            var path = ReadNSString(objc_msgSend_IntPtr(url, SelRegisterName("path")));
            return string.IsNullOrWhiteSpace(path)
                ? Array.Empty<string>()
                : new[] { path };
        }

        private static IntPtr CreateExtensionArray(ExtensionFilter[] extensions)
        {
            if (extensions == null || extensions.Length == 0)
            {
                return IntPtr.Zero;
            }

            var mutableArrayClass = objc_getClass("NSMutableArray");
            if (mutableArrayClass == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var array = objc_msgSend_IntPtr(mutableArrayClass, SelRegisterName("array"));
            if (array == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            foreach (var filter in extensions)
            {
                if (filter.Extensions == null)
                {
                    continue;
                }

                foreach (var extension in filter.Extensions)
                {
                    if (string.IsNullOrWhiteSpace(extension))
                    {
                        continue;
                    }

                    var ext = extension.Trim().TrimStart('.');
                    if (ext.Length == 0)
                    {
                        continue;
                    }

                    var nsString = CreateNSString(ext);
                    if (nsString != IntPtr.Zero)
                    {
                        objc_msgSend_void_IntPtr(array, SelRegisterName("addObject:"), nsString);
                    }
                }
            }

            return array;
        }

        private static IntPtr CreateNSString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return IntPtr.Zero;
            }

            var nsStringClass = objc_getClass("NSString");
            if (nsStringClass == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var utf8 = System.Text.Encoding.UTF8.GetBytes($"{value}\0");
            var unmanaged = Marshal.AllocHGlobal(utf8.Length);
            try
            {
                Marshal.Copy(utf8, 0, unmanaged, utf8.Length);
                return objc_msgSend_IntPtr_IntPtr(
                    nsStringClass,
                    SelRegisterName("stringWithUTF8String:"),
                    unmanaged);
            }
            finally
            {
                Marshal.FreeHGlobal(unmanaged);
            }
        }

        private static string ReadNSString(IntPtr nsString)
        {
            if (nsString == IntPtr.Zero)
            {
                return string.Empty;
            }

            var utf8String = objc_msgSend_IntPtr(nsString, SelRegisterName("UTF8String"));
            return utf8String == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringAnsi(utf8String) ?? string.Empty;
        }
    }
}

#endif
