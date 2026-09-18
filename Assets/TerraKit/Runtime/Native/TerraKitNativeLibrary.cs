using System;
using System.Runtime.InteropServices;

namespace TerraKit
{
    /// <summary>
    /// Performs a small, non-destructive health check against the TerraKit native library.
    /// The rest of the graph runtime can use this before creating registries or pipelines.
    /// </summary>
    public static class TerraKitNativeLibrary
    {
        public const uint SupportedAbiMajor = 0;
        public const uint SupportedAbiMinor = 1;

        public static TerraKitNativeStatus CheckAvailability()
        {
            try
            {
                TerraKitNativeVersion abiVersion;
                int status = TerraKitNativeMethods.GetAbiVersion(out abiVersion);
                if (status != TerraKitNativeStatusCode.Ok)
                {
                    return TerraKitNativeStatus.Failure(
                        status,
                        "The TerraKit library loaded, but tk_get_abi_version failed.",
                        TerraKitNativeMethods.CopyLastError());
                }

                TerraKitNativeStringView libraryVersionView;
                status = TerraKitNativeMethods.GetLibraryVersion(out libraryVersionView);
                if (status != TerraKitNativeStatusCode.Ok)
                {
                    return TerraKitNativeStatus.Failure(
                        status,
                        "The TerraKit ABI version was read, but the library version could not be read.",
                        TerraKitNativeMethods.CopyLastError(),
                        abiVersion);
                }

                string libraryVersion = libraryVersionView.CopyUtf8();
                if (abiVersion.Major != SupportedAbiMajor || abiVersion.Minor != SupportedAbiMinor)
                {
                    return TerraKitNativeStatus.Failure(
                        TerraKitNativeStatusCode.AbiVersionMismatch,
                        "The TerraKit native library uses an incompatible ABI version.",
                        "Unity supports ABI " + SupportedAbiMajor + "." +
                        SupportedAbiMinor + ".x, but the loaded library reports " +
                        abiVersion + ".",
                        abiVersion,
                        libraryVersion);
                }

                return TerraKitNativeStatus.Success(abiVersion, libraryVersion);
            }
            catch (DllNotFoundException exception)
            {
                return TerraKitNativeStatus.Failure(
                    TerraKitNativeStatusCode.LibraryNotFound,
                    "The TerraKit native library is not installed for this Unity platform.",
                    exception.Message);
            }
            catch (EntryPointNotFoundException exception)
            {
                return TerraKitNativeStatus.Failure(
                    TerraKitNativeStatusCode.EntryPointNotFound,
                    "A TerraKit library was found, but it does not expose the expected ABI functions.",
                    exception.Message);
            }
            catch (BadImageFormatException exception)
            {
                return TerraKitNativeStatus.Failure(
                    TerraKitNativeStatusCode.InvalidLibraryArchitecture,
                    "The TerraKit library architecture does not match the Unity Editor architecture.",
                    exception.Message);
            }
            catch (Exception exception)
            {
                return TerraKitNativeStatus.Failure(
                    TerraKitNativeStatusCode.UnexpectedManagedError,
                    "Unity could not complete the TerraKit native-library check.",
                    exception.ToString());
            }
        }
    }

    public sealed class TerraKitNativeStatus
    {
        public bool IsAvailable { get; private set; }
        public int StatusCode { get; private set; }
        public string Summary { get; private set; }
        public string Details { get; private set; }
        public TerraKitNativeVersion AbiVersion { get; private set; }
        public string LibraryVersion { get; private set; }

        private TerraKitNativeStatus()
        {
        }

        internal static TerraKitNativeStatus Success(
            TerraKitNativeVersion abiVersion,
            string libraryVersion)
        {
            return new TerraKitNativeStatus
            {
                IsAvailable = true,
                StatusCode = TerraKitNativeStatusCode.Ok,
                Summary = "TerraKit backend connection succeeded.",
                Details = "ABI " + abiVersion + ", library " + libraryVersion + ".",
                AbiVersion = abiVersion,
                LibraryVersion = libraryVersion
            };
        }

        internal static TerraKitNativeStatus Failure(
            int statusCode,
            string summary,
            string details,
            TerraKitNativeVersion abiVersion = default(TerraKitNativeVersion),
            string libraryVersion = "")
        {
            return new TerraKitNativeStatus
            {
                IsAvailable = false,
                StatusCode = statusCode,
                Summary = summary,
                Details = string.IsNullOrEmpty(details) ? "No additional diagnostic was provided." : details,
                AbiVersion = abiVersion,
                LibraryVersion = libraryVersion ?? string.Empty
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TerraKitNativeVersion
    {
        public uint Major;
        public uint Minor;
        public uint Patch;

        public override string ToString()
        {
            return Major + "." + Minor + "." + Patch;
        }
    }

    internal static class TerraKitNativeStatusCode
    {
        public const int Ok = 0;

        // Managed-only diagnostics. Native TerraKit status codes are non-negative.
        public const int LibraryNotFound = -1001;
        public const int EntryPointNotFound = -1002;
        public const int InvalidLibraryArchitecture = -1003;
        public const int AbiVersionMismatch = -1004;
        public const int UnexpectedManagedError = -1099;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TerraKitNativeStringView
    {
        public IntPtr Data;
        public UIntPtr Length;

        public string CopyUtf8()
        {
            ulong byteCount = Length.ToUInt64();
            if (Data == IntPtr.Zero || byteCount == 0)
            {
                return string.Empty;
            }

            if (byteCount > int.MaxValue)
            {
                throw new InvalidOperationException("TerraKit returned a string larger than Unity can copy.");
            }

            var bytes = new byte[(int)byteCount];
            Marshal.Copy(Data, bytes, 0, bytes.Length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }

    internal static class TerraKitNativeMethods
    {
        private const string LibraryName = "terrakit";

        [DllImport(
            LibraryName,
            EntryPoint = "tk_get_abi_version",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int GetAbiVersion(out TerraKitNativeVersion version);

        [DllImport(
            LibraryName,
            EntryPoint = "tk_get_library_version",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        internal static extern int GetLibraryVersion(out TerraKitNativeStringView version);

        [DllImport(
            LibraryName,
            EntryPoint = "tk_last_error_message_copy",
            CallingConvention = CallingConvention.Cdecl,
            ExactSpelling = true)]
        private static extern int LastErrorMessageCopy(
            IntPtr buffer,
            UIntPtr capacity,
            out UIntPtr required);

        internal static string CopyLastError()
        {
            UIntPtr required;
            int status = LastErrorMessageCopy(IntPtr.Zero, UIntPtr.Zero, out required);
            ulong requiredBytes = required.ToUInt64();
            if (status != TerraKitNativeStatusCode.Ok || requiredBytes <= 1)
            {
                return string.Empty;
            }

            if (requiredBytes > int.MaxValue)
            {
                return "The TerraKit error message was too large to copy.";
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)requiredBytes);
            try
            {
                status = LastErrorMessageCopy(buffer, required, out required);
                if (status != TerraKitNativeStatusCode.Ok)
                {
                    return "TerraKit failed to copy its native diagnostic (status " + status + ").";
                }

                int textLength = Math.Max(0, (int)required.ToUInt64() - 1);
                var bytes = new byte[textLength];
                if (textLength > 0)
                {
                    Marshal.Copy(buffer, bytes, 0, textLength);
                }

                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
