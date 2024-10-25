/*
 * Copyright (c) 2024 Snowflake Computing Inc. All rights reserved.
 */


using System.IO;
using System.Security;
using System.Text;
using Mono.Unix;
using Mono.Unix.Native;

namespace Snowflake.Data.Core.Tools
{

    internal class UnixOperations
    {
        public static readonly UnixOperations Instance = new UnixOperations();

        public virtual int CreateFileWithPermissions(string path, FilePermissions permissions)
        {
            return Syscall.creat(path, permissions);
        }

        public virtual int CreateDirectoryWithPermissions(string path, FilePermissions permissions)
        {
            return Syscall.mkdir(path, permissions);
        }

        public virtual FileAccessPermissions GetFilePermissions(string path)
        {
            var fileInfo = new UnixFileInfo(path);
            return fileInfo.FileAccessPermissions;
        }

        public virtual FileAccessPermissions GetDirPermissions(string path)
        {
            var dirInfo = new UnixDirectoryInfo(path);
            return dirInfo.FileAccessPermissions;
        }

        public virtual bool CheckFileHasAnyOfPermissions(string path, FileAccessPermissions permissions)
        {
            var fileInfo = new UnixFileInfo(path);
            return (permissions & fileInfo.FileAccessPermissions) != 0;
        }

        public string ReadAllText(string path, FileAccessPermissions forbiddenPermissions = FileAccessPermissions.OtherReadWriteExecute)
        {
            var fileInfo = new UnixFileInfo(path: path);

            using (var handle = fileInfo.OpenRead())
            {
                if (handle.OwnerUser.UserId != Syscall.geteuid())
                    throw new SecurityException("Attempting to read a file not owned by the effective user of the current process");
                if (handle.OwnerGroup.GroupId != Syscall.getegid())
                    throw new SecurityException("Attempting to read a file not owned by the effective group of the current process");
                if ((handle.FileAccessPermissions & forbiddenPermissions) != 0)
                    throw new SecurityException("Attempting to read a file with too broad permissions assigned");
                using (var streamReader = new StreamReader(handle, Encoding.Default))
                {
                    return streamReader.ReadToEnd();
                }
            }
        }
    }
}
