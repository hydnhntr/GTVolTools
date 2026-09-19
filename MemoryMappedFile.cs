using System;
using System.IO;
#if NET
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using MMF = System.IO.MemoryMappedFiles.MemoryMappedFile;
#else
using System.Runtime.InteropServices;
#endif

namespace GT2Vol
{
    // GTVolTool (.NET 8) uses System.IO.MemoryMappedFiles to support macOS and Linux.
    // GTVolToolGui (.NET 2.0) shares this file and retains the kernel32 calls.
    public static class MemoryMappedFile
    {
#if NET
        private class Mapping
        {
            public MMF File;
            public MemoryMappedViewAccessor Accessor;
        }

        // Callers only hold the raw pointer, so this stops the MMF and accessor being garbage collected before UnMap.
        private static readonly Dictionary<IntPtr, Mapping> Mappings = new Dictionary<IntPtr, Mapping>();

        public static unsafe IntPtr Map(FileStream file)
        {
            MMF mmf = MMF.CreateFromFile(file, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
            MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            byte* pointer = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            // AcquirePointer returns the page aligned base, PointerOffset moves it to the view’s start
            IntPtr address = new IntPtr(pointer + accessor.PointerOffset);

            Mappings[address] = new Mapping { File = mmf, Accessor = accessor };
            return address;
        }

        public static void UnMap(IntPtr pFile)
        {
            if (pFile == IntPtr.Zero)
            {
                return;
            }

            if (Mappings.TryGetValue(pFile, out Mapping mapping))
            {
                mapping.Accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                mapping.Accessor.Dispose();
                mapping.File.Dispose();
                Mappings.Remove(pFile);
            }
        }
#else
        private static uint PAGE_READONLY = 0x2;
        private static uint FILE_MAP_READ = 0x4;

        [DllImport("kernel32.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
        private extern static IntPtr CreateFileMappingW(
            IntPtr fileHandle,
            IntPtr sec,
            uint protect,
            uint maxHigh,
            uint maxLow,
            [MarshalAs(UnmanagedType.LPWStr)]
            string name
        );

        [DllImport("kernel32.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
        private extern static int UnmapViewOfFile(IntPtr pFile);

        [DllImport("kernel32.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
        private extern static IntPtr MapViewOfFileEx(
            IntPtr mapHandle,
            uint access,
            uint offLow,
            uint offHigh,
            IntPtr bytesToMap,
            IntPtr baseAddress
        );

        [DllImport("kernel32.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern int CloseHandle(IntPtr handle);

        public static IntPtr Map(FileStream file)
        {
            IntPtr fileHandle = file.SafeFileHandle.DangerousGetHandle();
            IntPtr mapHandle = CreateFileMappingW(fileHandle, IntPtr.Zero, PAGE_READONLY, 0, 0, null);
            if (mapHandle != IntPtr.Zero)
            {
                IntPtr pFile = MapViewOfFileEx(mapHandle, FILE_MAP_READ, 0, 0, IntPtr.Zero, IntPtr.Zero);
                CloseHandle(mapHandle);
                mapHandle = pFile;
            }
            return mapHandle;
        }

        public static void UnMap(IntPtr pFile)
        {
            UnmapViewOfFile(pFile);
        }
#endif
    }
}
