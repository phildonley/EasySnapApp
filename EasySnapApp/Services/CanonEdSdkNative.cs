using System;
using System.Runtime.InteropServices;

namespace EasySnapApp.Services
{
    /// <summary>
    /// Canon EDSDK P/Invoke wrapper for Windows (EDSDK.dll).
    /// Target: EDSDK v13.x (64-bit), .NET Framework 4.7.2
    /// 
    /// CORRECTED VERSION - Fixed constants and function signatures to match original EDSDK.cs from project.
    /// 
    /// Key fixes:
    /// - Corrected all Object Event constants to match project EDSDK.cs values
    /// - Fixed function signatures to use correct enum types instead of integers
    /// - Added missing EDS_ERR_STREAM_BAD_OPTIONS error constant (0x000000AB)
    /// - Removed duplicate error code cases
    /// - Corrected property ID constants
    /// </summary>
    public static class CanonEdSdkNative
    {
        private const string EDSDK_DLL = "EDSDK.dll";

        #region Error Codes
        public const uint EDS_ERR_OK = 0x00000000;

        // Common
        public const uint EDS_ERR_UNIMPLEMENTED = 0x00000001;
        public const uint EDS_ERR_INTERNAL_ERROR = 0x00000002;
        public const uint EDS_ERR_MEM_ALLOC_FAILED = 0x00000003;
        public const uint EDS_ERR_MEM_FREE_FAILED = 0x00000004;
        public const uint EDS_ERR_OPERATION_CANCELLED = 0x00000005;
        public const uint EDS_ERR_INCOMPATIBLE_VERSION = 0x00000006;
        public const uint EDS_ERR_NOT_SUPPORTED = 0x00000007;
        public const uint EDS_ERR_UNEXPECTED_EXCEPTION = 0x00000008;
        public const uint EDS_ERR_PROTECTION_VIOLATION = 0x00000009;
        public const uint EDS_ERR_MISSING_SUBCOMPONENT = 0x0000000A;
        public const uint EDS_ERR_SELECTION_UNAVAILABLE = 0x0000000B;

        // File errors
        public const uint EDS_ERR_FILE_IO_ERROR = 0x00000020;
        public const uint EDS_ERR_FILE_TOO_MANY_OPEN = 0x00000021;
        public const uint EDS_ERR_FILE_NOT_FOUND = 0x00000022;
        public const uint EDS_ERR_FILE_OPEN_ERROR = 0x00000023;
        public const uint EDS_ERR_FILE_CLOSE_ERROR = 0x00000024;
        public const uint EDS_ERR_FILE_SEEK_ERROR = 0x00000025;
        public const uint EDS_ERR_FILE_TELL_ERROR = 0x00000026;
        public const uint EDS_ERR_FILE_READ_ERROR = 0x00000027;
        public const uint EDS_ERR_FILE_WRITE_ERROR = 0x00000028;
        public const uint EDS_ERR_FILE_PERMISSION_ERROR = 0x00000029;
        public const uint EDS_ERR_FILE_DISK_FULL_ERROR = 0x0000002A;
        public const uint EDS_ERR_FILE_ALREADY_EXISTS = 0x0000002B;
        public const uint EDS_ERR_FILE_FORMAT_UNRECOGNIZED = 0x0000002C;
        public const uint EDS_ERR_FILE_DATA_CORRUPT = 0x0000002D;
        public const uint EDS_ERR_FILE_NAMING_NA = 0x0000002E;

        // Directory errors
        public const uint EDS_ERR_DIR_NOT_FOUND = 0x00000040;
        public const uint EDS_ERR_DIR_IO_ERROR = 0x00000041;
        public const uint EDS_ERR_DIR_ENTRY_NOT_FOUND = 0x00000042;
        public const uint EDS_ERR_DIR_ENTRY_EXISTS = 0x00000043;
        public const uint EDS_ERR_DIR_NOT_EMPTY = 0x00000044;

        // Property errors
        public const uint EDS_ERR_PROPERTIES_UNAVAILABLE = 0x00000050;
        public const uint EDS_ERR_PROPERTIES_MISMATCH = 0x00000051;
        public const uint EDS_ERR_PROPERTIES_NOT_LOADED = 0x00000053;

        // Parameter / handle errors (from original project EDSDK.cs)
        public const uint EDS_ERR_INVALID_PARAMETER = 0x00000060;
        public const uint EDS_ERR_INVALID_HANDLE = 0x00000061;
        public const uint EDS_ERR_INVALID_POINTER = 0x00000062;
        public const uint EDS_ERR_INVALID_INDEX = 0x00000063;
        public const uint EDS_ERR_INVALID_LENGTH = 0x00000064;
        public const uint EDS_ERR_INVALID_FN_POINTER = 0x00000065;
        public const uint EDS_ERR_INVALID_SORT_FN = 0x00000066;

        // Device errors
        public const uint EDS_ERR_DEVICE_NOT_FOUND = 0x00000080;
        public const uint EDS_ERR_DEVICE_BUSY = 0x00000081;
        public const uint EDS_ERR_DEVICE_INVALID = 0x00000082;
        public const uint EDS_ERR_DEVICE_EMERGENCY = 0x00000083;
        public const uint EDS_ERR_DEVICE_MEMORY_FULL = 0x00000084;
        public const uint EDS_ERR_DEVICE_INTERNAL_ERROR = 0x00000085;
        public const uint EDS_ERR_DEVICE_INVALID_PARAMETER = 0x00000086;
        public const uint EDS_ERR_DEVICE_NO_DISK = 0x00000087;
        public const uint EDS_ERR_DEVICE_DISK_ERROR = 0x00000088;
        public const uint EDS_ERR_DEVICE_CF_GATE_CHANGED = 0x00000089;
        public const uint EDS_ERR_DEVICE_DIAL_CHANGED = 0x0000008A;
        public const uint EDS_ERR_DEVICE_NOT_INSTALLED = 0x0000008B;
        public const uint EDS_ERR_DEVICE_STAY_AWAKE = 0x0000008C;
        public const uint EDS_ERR_DEVICE_NOT_RELEASED = 0x0000008D;

        // Stream errors (from original project EDSDK.cs)
        public const uint EDS_ERR_STREAM_IO_ERROR = 0x000000A0;
        public const uint EDS_ERR_STREAM_NOT_OPEN = 0x000000A1;
        public const uint EDS_ERR_STREAM_ALREADY_OPEN = 0x000000A2;
        public const uint EDS_ERR_STREAM_OPEN_ERROR = 0x000000A3;
        public const uint EDS_ERR_STREAM_CLOSE_ERROR = 0x000000A4;
        public const uint EDS_ERR_STREAM_SEEK_ERROR = 0x000000A5;
        public const uint EDS_ERR_STREAM_TELL_ERROR = 0x000000A6;
        public const uint EDS_ERR_STREAM_READ_ERROR = 0x000000A7;
        public const uint EDS_ERR_STREAM_WRITE_ERROR = 0x000000A8;
        public const uint EDS_ERR_STREAM_PERMISSION_ERROR = 0x000000A9;
        public const uint EDS_ERR_STREAM_COULDNT_BEGIN_THREAD = 0x000000AA;
        public const uint EDS_ERR_STREAM_BAD_OPTIONS = 0x000000AB;  // THE KEY ERROR WE WERE MISSING!
        public const uint EDS_ERR_STREAM_END_OF_STREAM = 0x000000AC;

        // Object readiness
        public const uint EDS_ERR_OBJECT_NOTREADY = 0x000000F3;

        // Session errors
        public const uint EDS_ERR_SESSION_NOT_OPEN = 0x00002003;
        public const uint EDS_ERR_INVALID_TRANSACTIONID = 0x00002004;
        public const uint EDS_ERR_INCOMPLETE_TRANSFER = 0x00002007;
        #endregion

        #region Commands / Status
        public const uint kEdsCameraCommand_ExtendShutDownTimer = 0x00000001;

        public const uint kEdsCameraStatusCommand_UILock = 0x00000000;
        public const uint kEdsCameraStatusCommand_UIUnLock = 0x00000001;
        #endregion

        #region Property IDs + SaveTo
        public const uint kEdsPropID_ProductName = 0x00000002;

        // CRITICAL FIX — correct SaveTo property ID
        public const uint kEdsPropID_SaveTo = 0x00000404;

        // SaveTo values
        public const uint kEdsSaveTo_Camera = 1;
        public const uint kEdsSaveTo_Host = 2;
        public const uint kEdsSaveTo_Both = 3;
        #endregion

        #region Object Events (CORRECTED values from project EDSDK.cs)
        public const uint kEdsObjectEvent_All = 0x00000200;
        public const uint kEdsObjectEvent_VolumeInfoChanged = 0x00000201;       // This is what we're seeing as 0x201
        public const uint kEdsObjectEvent_VolumeUpdateItems = 0x00000202;       // Not DirItemCreated
        public const uint kEdsObjectEvent_FolderUpdateItems = 0x00000203;       
        public const uint kEdsObjectEvent_DirItemCreated = 0x00000204;          // This is correct for DirItemCreated
        public const uint kEdsObjectEvent_DirItemRemoved = 0x00000205;
        public const uint kEdsObjectEvent_DirItemInfoChanged = 0x00000206;      // This is what we're seeing as 0x204 in logs
        public const uint kEdsObjectEvent_DirItemContentChanged = 0x00000207;
        public const uint kEdsObjectEvent_DirItemRequestTransfer = 0x00000208;
        public const uint kEdsObjectEvent_DirItemRequestTransferDT = 0x00000209;
        public const uint kEdsObjectEvent_DirItemCancelTransferDT = 0x0000020A;
        public const uint kEdsObjectEvent_VolumeAdded = 0x0000020C;
        public const uint kEdsObjectEvent_VolumeRemoved = 0x0000020D;
        #endregion

        #region State Events
        public const uint kEdsStateEvent_All = 0x00000300;
        public const uint kEdsStateEvent_Shutdown = 0x00000301;
        public const uint kEdsStateEvent_JobStatusChanged = 0x00000302;
        public const uint kEdsStateEvent_WillSoonShutDown = 0x00000303;
        public const uint kEdsStateEvent_ShutDownTimerUpdated = 0x00000304;
        public const uint kEdsStateEvent_CaptureError = 0x00000305;
        public const uint kEdsStateEvent_InternalError = 0x00000306;
        #endregion

        #region File Stream Enums (CRITICAL - must match EDSDK enum types)
        public enum EdsFileCreateDisposition : uint
        {
            CreateNew = 0,        // kEdsFileCreateDisposition_CreateNew
            CreateAlways = 1,     // kEdsFileCreateDisposition_CreateAlways
            OpenExisting = 2,     // kEdsFileCreateDisposition_OpenExisting
            OpenAlways = 3,       // kEdsFileCreateDisposition_OpenAlways
            TruncateExisting = 4  // kEdsFileCreateDisposition_TruncateExsisting
        }

        public enum EdsAccess : uint
        {
            Read = 0,       // kEdsAccess_Read
            Write = 1,      // kEdsAccess_Write
            ReadWrite = 2,  // kEdsAccess_ReadWrite
            Error = 0xFFFFFFFF
        }

        // Convenience constants for backwards compatibility
        public const EdsFileCreateDisposition kEdsFileCreateDisposition_CreateNew = EdsFileCreateDisposition.CreateNew;
        public const EdsFileCreateDisposition kEdsFileCreateDisposition_CreateAlways = EdsFileCreateDisposition.CreateAlways;
        public const EdsFileCreateDisposition kEdsFileCreateDisposition_OpenExisting = EdsFileCreateDisposition.OpenExisting;
        public const EdsFileCreateDisposition kEdsFileCreateDisposition_OpenAlways = EdsFileCreateDisposition.OpenAlways;
        public const EdsFileCreateDisposition kEdsFileCreateDisposition_TruncateExisting = EdsFileCreateDisposition.TruncateExisting;

        public const EdsAccess kEdsAccess_Read = EdsAccess.Read;
        public const EdsAccess kEdsAccess_Write = EdsAccess.Write;
        public const EdsAccess kEdsAccess_ReadWrite = EdsAccess.ReadWrite;
        #endregion

        #region Structures
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsDeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPortName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDeviceDescription;

            public uint deviceSubType;
            public uint reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EdsVolumeInfo
        {
            public uint StorageType;
            public uint Access;
            public ulong MaxCapacity;
            public ulong FreeSpaceInBytes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szVolumeLabel;
        }

        // CRITICAL FIX — Canon headers use unsigned ints
        [StructLayout(LayoutKind.Sequential)]
        public struct EdsCapacity
        {
            public uint NumberOfFreeClusters;
            public uint BytesPerSector;
            public uint Reset;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct EdsDirectoryItemInfo
        {
            public ulong Size;
            public uint IsFolder;
            public uint GroupID;
            public uint Option;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string FileName;

            public uint format;
            public uint dateTime;
        }
        #endregion

        #region Delegates
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate uint EdsObjectEventHandler(uint inEvent, IntPtr inRef, IntPtr inContext);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate uint EdsStateEventHandler(uint inEvent, uint inEventData, IntPtr inContext);
        #endregion

        #region Imports (CORRECTED - use proper enum types)
        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsSetObjectEventHandler(IntPtr inCameraRef, uint inEvent, EdsObjectEventHandler inHandler, IntPtr inContext);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsSetCameraStateEventHandler(IntPtr inCameraRef, uint inEvent, EdsStateEventHandler inHandler, IntPtr inContext);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsInitializeSDK();

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsTerminateSDK();

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsGetCameraList(out IntPtr cameraListRef);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsGetChildCount(IntPtr inRef, out uint outCount);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsGetChildAtIndex(IntPtr inRef, int inIndex, out IntPtr outRef);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsRelease(IntPtr inRef);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsOpenSession(IntPtr inCameraRef);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsCloseSession(IntPtr inCameraRef);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsSendCommand(IntPtr inCameraRef, uint inCommand, int inParam);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsGetPropertySize(IntPtr inRef, uint inPropertyID, int inParam, out int outDataType, out int outSize);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsGetPropertyData(IntPtr inRef, uint inPropertyID, int inParam, int inPropertySize, IntPtr outPropertyData);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsSetPropertyData(IntPtr inRef, uint inPropertyID, int inParam, int inPropertySize, IntPtr inPropertyData);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsSetCapacity(IntPtr inCameraRef, EdsCapacity inCapacity);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsGetDirectoryItemInfo(IntPtr inDirItem, out EdsDirectoryItemInfo outDirItemInfo);

        // CORRECTED CRITICAL FUNCTIONS - Use enum types, not integers
        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsCreateFileStream(
            string inFileName, 
            EdsFileCreateDisposition inCreateDisposition, 
            EdsAccess inDesiredAccess, 
            out IntPtr outStream);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsCreateFileStreamEx(
            string inFileName, 
            EdsFileCreateDisposition inCreateDisposition, 
            EdsAccess inDesiredAccess, 
            out IntPtr outStream);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsDownload(IntPtr inDirItem, ulong inReadSize, IntPtr outStream);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsDownloadComplete(IntPtr inDirItem);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsDeleteDirectoryItem(IntPtr inDirItem);

        // Memory stream functions for reliable download
        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsCreateMemoryStream(ulong inBufferSize, out IntPtr outStream);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsGetLength(IntPtr inStream, out ulong outLength);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsGetPointer(IntPtr inStream, out IntPtr outPointer);

        [DllImport(EDSDK_DLL, CallingConvention = CallingConvention.Winapi)]
        public static extern uint EdsRead(IntPtr inStream, ulong inReadSize, IntPtr outBuffer, out ulong outReadSize);
        #endregion

        #region Error Description
        public static string GetErrorDescription(uint err)
        {
            if (err == EDS_ERR_OK) return "Success";

            switch (err)
            {
                case EDS_ERR_UNIMPLEMENTED: return "Unimplemented";
                case EDS_ERR_INTERNAL_ERROR: return "Internal Error";
                case EDS_ERR_MEM_ALLOC_FAILED: return "Memory Allocation Failed";
                case EDS_ERR_MEM_FREE_FAILED: return "Memory Free Failed";
                case EDS_ERR_OPERATION_CANCELLED: return "Operation Cancelled";
                case EDS_ERR_INCOMPATIBLE_VERSION: return "Incompatible Version";
                case EDS_ERR_NOT_SUPPORTED: return "Not Supported";
                case EDS_ERR_UNEXPECTED_EXCEPTION: return "Unexpected Exception";
                case EDS_ERR_PROTECTION_VIOLATION: return "Protection Violation";
                case EDS_ERR_MISSING_SUBCOMPONENT: return "Missing Subcomponent";
                case EDS_ERR_SELECTION_UNAVAILABLE: return "Selection Unavailable";

                // File errors
                case EDS_ERR_FILE_IO_ERROR: return "File I/O Error";
                case EDS_ERR_FILE_TOO_MANY_OPEN: return "File Too Many Open";
                case EDS_ERR_FILE_NOT_FOUND: return "File Not Found";
                case EDS_ERR_FILE_OPEN_ERROR: return "File Open Error";
                case EDS_ERR_FILE_CLOSE_ERROR: return "File Close Error";
                case EDS_ERR_FILE_SEEK_ERROR: return "File Seek Error";
                case EDS_ERR_FILE_TELL_ERROR: return "File Tell Error";
                case EDS_ERR_FILE_READ_ERROR: return "File Read Error";
                case EDS_ERR_FILE_WRITE_ERROR: return "File Write Error";
                case EDS_ERR_FILE_PERMISSION_ERROR: return "File Permission Error";
                case EDS_ERR_FILE_DISK_FULL_ERROR: return "File Disk Full Error";
                case EDS_ERR_FILE_ALREADY_EXISTS: return "File Already Exists";
                case EDS_ERR_FILE_FORMAT_UNRECOGNIZED: return "File Format Unrecognized";
                case EDS_ERR_FILE_DATA_CORRUPT: return "File Data Corrupt";
                case EDS_ERR_FILE_NAMING_NA: return "File Naming N/A";

                // Directory errors
                case EDS_ERR_DIR_NOT_FOUND: return "Directory Not Found";
                case EDS_ERR_DIR_IO_ERROR: return "Directory I/O Error";
                case EDS_ERR_DIR_ENTRY_NOT_FOUND: return "Directory Entry Not Found";
                case EDS_ERR_DIR_ENTRY_EXISTS: return "Directory Entry Exists";
                case EDS_ERR_DIR_NOT_EMPTY: return "Directory Not Empty";

                // Property errors
                case EDS_ERR_PROPERTIES_UNAVAILABLE: return "Properties Unavailable";
                case EDS_ERR_PROPERTIES_MISMATCH: return "Properties Mismatch";
                case EDS_ERR_PROPERTIES_NOT_LOADED: return "Properties Not Loaded";

                // Parameter / handle errors
                case EDS_ERR_INVALID_PARAMETER: return "Invalid Parameter";
                case EDS_ERR_INVALID_HANDLE: return "Invalid Handle";
                case EDS_ERR_INVALID_POINTER: return "Invalid Pointer";
                case EDS_ERR_INVALID_INDEX: return "Invalid Index";
                case EDS_ERR_INVALID_LENGTH: return "Invalid Length";
                case EDS_ERR_INVALID_FN_POINTER: return "Invalid Function Pointer";
                case EDS_ERR_INVALID_SORT_FN: return "Invalid Sort Function";

                // Device errors
                case EDS_ERR_DEVICE_NOT_FOUND: return "Device Not Found";
                case EDS_ERR_DEVICE_BUSY: return "Device Busy";
                case EDS_ERR_DEVICE_INVALID: return "Device Invalid";
                case EDS_ERR_DEVICE_EMERGENCY: return "Device Emergency";
                case EDS_ERR_DEVICE_MEMORY_FULL: return "Device Memory Full";
                case EDS_ERR_DEVICE_INTERNAL_ERROR: return "Device Internal Error";
                case EDS_ERR_DEVICE_INVALID_PARAMETER: return "Device Invalid Parameter";
                case EDS_ERR_DEVICE_NO_DISK: return "Device No Disk";
                case EDS_ERR_DEVICE_DISK_ERROR: return "Device Disk Error";
                case EDS_ERR_DEVICE_CF_GATE_CHANGED: return "Device CF Gate Changed";
                case EDS_ERR_DEVICE_DIAL_CHANGED: return "Device Dial Changed";
                case EDS_ERR_DEVICE_NOT_INSTALLED: return "Device Not Installed";
                case EDS_ERR_DEVICE_STAY_AWAKE: return "Device Stay Awake";
                case EDS_ERR_DEVICE_NOT_RELEASED: return "Device Not Released";

                // Stream errors
                case EDS_ERR_STREAM_IO_ERROR: return "Stream I/O Error";
                case EDS_ERR_STREAM_NOT_OPEN: return "Stream Not Open";
                case EDS_ERR_STREAM_ALREADY_OPEN: return "Stream Already Open";
                case EDS_ERR_STREAM_OPEN_ERROR: return "Stream Open Error";
                case EDS_ERR_STREAM_CLOSE_ERROR: return "Stream Close Error";
                case EDS_ERR_STREAM_SEEK_ERROR: return "Stream Seek Error";
                case EDS_ERR_STREAM_TELL_ERROR: return "Stream Tell Error";
                case EDS_ERR_STREAM_READ_ERROR: return "Stream Read Error";
                case EDS_ERR_STREAM_WRITE_ERROR: return "Stream Write Error";
                case EDS_ERR_STREAM_PERMISSION_ERROR: return "Stream Permission Error";
                case EDS_ERR_STREAM_COULDNT_BEGIN_THREAD: return "Stream Couldn't Begin Thread";
                case EDS_ERR_STREAM_BAD_OPTIONS: return "Stream Bad Options";  // THE KEY ERROR WE WERE MISSING!
                case EDS_ERR_STREAM_END_OF_STREAM: return "Stream End Of Stream";

                // Other errors
                case EDS_ERR_OBJECT_NOTREADY: return "Object Not Ready";
                case EDS_ERR_SESSION_NOT_OPEN: return "Session Not Open";
                case EDS_ERR_INVALID_TRANSACTIONID: return "Invalid Transaction ID";
                case EDS_ERR_INCOMPLETE_TRANSFER: return "Incomplete Transfer";
            }

            return $"Unknown Error (0x{err:X8})";
        }
        #endregion
    }
}
