/*
 * Copyright (c) 2024 Snowflake Computing Inc. All rights reserved.
 */

using Microsoft.Win32.SafeHandles;
using Snowflake.Data.Client;
using Snowflake.Data.Log;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Snowflake.Data.Core.CredentialManager.Infrastructure
{
    using Tools;

    internal class SFCredentialManagerWindowsNativeImpl : ISnowflakeCredentialManager
    {
        private static readonly SFLogger s_logger = SFLoggerFactory.GetLogger<SFCredentialManagerWindowsNativeImpl>();

        public static readonly SFCredentialManagerWindowsNativeImpl Instance = new SFCredentialManagerWindowsNativeImpl();

        public string GetCredentials(string key)
        {
            s_logger.Debug($"Getting the credentials for key: {key}");
            var hashKey = key.ToSha256Hash();
            IntPtr nCredPtr;
            if (!CredRead(hashKey, 1 /* Generic */, 0, out nCredPtr))
            {
                s_logger.Info($"Unable to get credentials for key: {key}");
                return "";
            }

            var critCred = new CriticalCredentialHandle(nCredPtr);
            Credential cred = critCred.GetCredential();
            return cred.CredentialBlob;
        }

        public void RemoveCredentials(string key)
        {
            s_logger.Debug($"Removing the credentials for key: {key}");

            var hashKey = key.ToSha256Hash();
            if (!CredDelete(hashKey, 1 /* Generic */, 0))
            {
                s_logger.Info($"Unable to remove credentials because the specified key did not exist: {key}");
            }
        }

        public void SaveCredentials(string key, string token)
        {
            s_logger.Debug($"Saving the credentials for key: {key}");
            var hashKey = key.ToSha256Hash();
            byte[] byteArray = Encoding.Unicode.GetBytes(token);
            Credential credential = new Credential();
            credential.AttributeCount = 0;
            credential.Attributes = IntPtr.Zero;
            credential.Comment = IntPtr.Zero;
            credential.TargetAlias = IntPtr.Zero;
            credential.Type = 1; // Generic
            credential.Persist = 2; // Local Machine
            credential.CredentialBlobSize = (uint)(byteArray == null ? 0 : byteArray.Length);
            credential.TargetName = hashKey;
            credential.CredentialBlob = token;
            credential.UserName = Environment.UserName;

            CredWrite(ref credential, 0);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string UserName;
        }

        sealed class CriticalCredentialHandle : CriticalHandleZeroOrMinusOneIsInvalid
        {
            public CriticalCredentialHandle(IntPtr handle)
            {
                SetHandle(handle);
            }

            public Credential GetCredential()
            {
                var credential = (Credential)Marshal.PtrToStructure(handle, typeof(Credential));
                return credential;
            }

            protected override bool ReleaseHandle()
            {
                if (IsInvalid)
                {
                    return false;
                }

                CredFree(handle);
                SetHandleAsInvalid();
                return true;
            }
        }

        [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CredDelete(string target, uint type, int reservedFlag);

        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredRead(string target, uint type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredWrite([In] ref Credential userCredential, [In] uint flags);

        [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
        static extern bool CredFree([In] IntPtr cred);
    }
}
