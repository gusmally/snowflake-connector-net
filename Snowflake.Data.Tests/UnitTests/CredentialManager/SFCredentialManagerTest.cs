/*
 * Copyright (c) 2024 Snowflake Computing Inc. All rights reserved.
 */

namespace Snowflake.Data.Tests.UnitTests.CredentialManager
{
    using Mono.Unix;
    using Mono.Unix.Native;
    using Moq;
    using NUnit.Framework;
    using Snowflake.Data.Client;
    using Snowflake.Data.Core.CredentialManager;
    using Snowflake.Data.Core.CredentialManager.Infrastructure;
    using Snowflake.Data.Core.Tools;
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security;

    public abstract class SFBaseCredentialManagerTest
    {
        protected ISnowflakeCredentialManager _credentialManager;

        [Test]
        public void TestSavingAndRemovingCredentials()
        {
            // arrange
            var key = "mockKey";
            var expectedToken = "token";

            // act
            _credentialManager.SaveCredentials(key, expectedToken);

            // assert
            Assert.AreEqual(expectedToken, _credentialManager.GetCredentials(key));

            // act
            _credentialManager.RemoveCredentials(key);

            // assert
            Assert.IsTrue(string.IsNullOrEmpty(_credentialManager.GetCredentials(key)));
        }

        [Test]
        public void TestSavingCredentialsForAnExistingKey()
        {
            // arrange
            var key = "mockKey";
            var firstExpectedToken = "mockToken1";
            var secondExpectedToken = "mockToken2";

            try
            {
                // act
                _credentialManager.SaveCredentials(key, firstExpectedToken);

                // assert
                Assert.AreEqual(firstExpectedToken, _credentialManager.GetCredentials(key));

                // act
                _credentialManager.SaveCredentials(key, secondExpectedToken);

                // assert
                Assert.AreEqual(secondExpectedToken, _credentialManager.GetCredentials(key));

                // act
                _credentialManager.RemoveCredentials(key);

                // assert
                Assert.IsTrue(string.IsNullOrEmpty(_credentialManager.GetCredentials(key)));
            }
            catch (Exception ex)
            {
                // assert
                Assert.Fail("Should not throw an exception: " + ex.Message);
            }
        }

        [Test]
        public void TestRemovingCredentialsForKeyThatDoesNotExist()
        {
            // arrange
            var key = "mockKey";

            try
            {
                // act
                _credentialManager.RemoveCredentials(key);

                // assert
                Assert.IsTrue(string.IsNullOrEmpty(_credentialManager.GetCredentials(key)));
            }
            catch (Exception ex)
            {
                // assert
                Assert.Fail("Should not throw an exception: " + ex.Message);
            }
        }
    }

    [TestFixture]
    [Platform("Win")]
    public class SFNativeCredentialManagerTest : SFBaseCredentialManagerTest
    {
        [SetUp]
        public void SetUp()
        {
            _credentialManager = SFCredentialManagerWindowsNativeImpl.Instance;
        }
    }

    [TestFixture]
    public class SFInMemoryCredentialManagerTest : SFBaseCredentialManagerTest
    {
        [SetUp]
        public void SetUp()
        {
            _credentialManager = SFCredentialManagerInMemoryImpl.Instance;
        }
    }

    [TestFixture]
    public class SFFileCredentialManagerTest : SFBaseCredentialManagerTest
    {
        [SetUp]
        public void SetUp()
        {
            _credentialManager = SFCredentialManagerFileImpl.Instance;
        }
    }

    [TestFixture]
    class SFCredentialManagerTest
    {
        ISnowflakeCredentialManager _credentialManager;

        [ThreadStatic]
        private static Mock<FileOperations> t_fileOperations;

        [ThreadStatic]
        private static Mock<DirectoryOperations> t_directoryOperations;

        [ThreadStatic]
        private static Mock<UnixOperations> t_unixOperations;

        [ThreadStatic]
        private static Mock<EnvironmentOperations> t_environmentOperations;

        private const string CustomJsonDir = "testdirectory";

        private static readonly string s_customJsonPath = Path.Combine(CustomJsonDir, SFCredentialManagerFileImpl.CredentialCacheFileName);

        [SetUp] public void SetUp()
        {
            t_fileOperations = new Mock<FileOperations>();
            t_directoryOperations = new Mock<DirectoryOperations>();
            t_unixOperations = new Mock<UnixOperations>();
            t_environmentOperations = new Mock<EnvironmentOperations>();
            SFCredentialManagerFactory.SetCredentialManager(SFCredentialManagerInMemoryImpl.Instance);
        }

        [TearDown] public void TearDown()
        {
            SFCredentialManagerFactory.UseDefaultCredentialManager();
        }

        [Test]
        public void TestUsingDefaultCredentialManager()
        {
            // arrange
            SFCredentialManagerFactory.UseDefaultCredentialManager();

            // act
            _credentialManager = SFCredentialManagerFactory.GetCredentialManager();

            // assert
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.IsInstanceOf<SFCredentialManagerWindowsNativeImpl>(_credentialManager);
            }
            else
            {
                Assert.IsInstanceOf<SFCredentialManagerInMemoryImpl>(_credentialManager);
            }
        }

        [Test]
        public void TestSettingCustomCredentialManager()
        {
            // arrange
            SFCredentialManagerFactory.SetCredentialManager(SFCredentialManagerFileImpl.Instance);

            // act
            _credentialManager = SFCredentialManagerFactory.GetCredentialManager();

            // assert
            Assert.IsInstanceOf<SFCredentialManagerFileImpl>(_credentialManager);
        }

        [Test]
        public void TestThatThrowsErrorWhenCacheFileIsNotCreated()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("skip test on Windows");
            }

            // arrange
            t_directoryOperations
                .Setup(d => d.Exists(s_customJsonPath))
                .Returns(false);
            t_unixOperations
                .Setup(u => u.CreateFileWithPermissions(s_customJsonPath,
                    FilePermissions.S_IRUSR | FilePermissions.S_IWUSR | FilePermissions.S_IXUSR))
                .Returns(-1);
            t_environmentOperations
                .Setup(e => e.GetEnvironmentVariable(SFCredentialManagerFileImpl.CredentialCacheDirectoryEnvironmentName))
                .Returns(CustomJsonDir);
            SFCredentialManagerFactory.SetCredentialManager(new SFCredentialManagerFileImpl(t_fileOperations.Object, t_directoryOperations.Object, t_unixOperations.Object, t_environmentOperations.Object));
            _credentialManager = SFCredentialManagerFactory.GetCredentialManager();

            // act
            var thrown = Assert.Throws<Exception>(() => _credentialManager.SaveCredentials("key", "token"));

            // assert
            Assert.That(thrown.Message, Does.Contain("Failed to create the JSON token cache file"));
        }

        [Test]
        public void TestThatThrowsErrorWhenCacheFileCanBeAccessedByOthers()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("skip test on Windows");
            }

            // arrange
            t_unixOperations
                .Setup(u => u.CreateFileWithPermissions(s_customJsonPath,
                    FilePermissions.S_IRUSR | FilePermissions.S_IWUSR | FilePermissions.S_IXUSR))
                .Returns(0);
            t_unixOperations
                .Setup(u => u.GetFilePermissions(s_customJsonPath))
                .Returns(FileAccessPermissions.AllPermissions);
            t_environmentOperations
                .Setup(e => e.GetEnvironmentVariable(SFCredentialManagerFileImpl.CredentialCacheDirectoryEnvironmentName))
                .Returns(CustomJsonDir);
            SFCredentialManagerFactory.SetCredentialManager(new SFCredentialManagerFileImpl(t_fileOperations.Object, t_directoryOperations.Object, t_unixOperations.Object, t_environmentOperations.Object));
            _credentialManager = SFCredentialManagerFactory.GetCredentialManager();

            // act
            var thrown = Assert.Throws<Exception>(() => _credentialManager.SaveCredentials("key", "token"));

            // assert
            Assert.That(thrown.Message, Does.Contain("Permission for the JSON token cache file should contain only the owner access"));
        }

        [Test]
        public void TestThatJsonFileIsCheckedIfAlreadyExists()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("skip test on Windows");
            }

            // arrange
            t_unixOperations
                .Setup(u => u.CreateFileWithPermissions(s_customJsonPath,
                    FilePermissions.S_IRUSR | FilePermissions.S_IWUSR | FilePermissions.S_IXUSR))
                .Returns(0);
            t_unixOperations
                .Setup(u => u.GetFilePermissions(s_customJsonPath))
                .Returns(FileAccessPermissions.UserReadWriteExecute);
            t_environmentOperations
                .Setup(e => e.GetEnvironmentVariable(SFCredentialManagerFileImpl.CredentialCacheDirectoryEnvironmentName))
                .Returns(CustomJsonDir);
            t_fileOperations
                .SetupSequence(f => f.Exists(s_customJsonPath))
                .Returns(false)
                .Returns(true);

            SFCredentialManagerFactory.SetCredentialManager(new SFCredentialManagerFileImpl(t_fileOperations.Object, t_directoryOperations.Object, t_unixOperations.Object, t_environmentOperations.Object));
            _credentialManager = SFCredentialManagerFactory.GetCredentialManager();

            // act
            _credentialManager.SaveCredentials("key", "token");

            // assert
            t_fileOperations.Verify(f => f.Exists(s_customJsonPath), Times.Exactly(2));
        }

        [Test]
        public void TestThatJsonFileIsCheckedIfOwnedByCurrentUser()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("skip test on Windows");
            }

            // arrange
            t_unixOperations
                .Setup(u => u.CheckFileIsNotOwnedByCurrentUser(s_customJsonPath))
                .Returns(true);
            t_environmentOperations
                .Setup(e => e.GetEnvironmentVariable(SFCredentialManagerFileImpl.CredentialCacheDirectoryEnvironmentName))
                .Returns(CustomJsonDir);
            t_fileOperations
                .SetupSequence(f => f.Exists(s_customJsonPath))
                .Returns(true);

            SFCredentialManagerFactory.SetCredentialManager(new SFCredentialManagerFileImpl(t_fileOperations.Object, t_directoryOperations.Object, t_unixOperations.Object, t_environmentOperations.Object));
            _credentialManager = SFCredentialManagerFactory.GetCredentialManager();

            // act
            var thrown = Assert.Throws<SecurityException>(() => _credentialManager.GetCredentials("key"));

            // assert
            Assert.That(thrown.Message, Does.Contain("Attempting to read a file not owned by the effective user of the current process"));
        }

        [Test]
        public void TestThatJsonFileIsCheckedIfOwnedByCurrentGroup()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("skip test on Windows");
            }

            // arrange
            t_unixOperations
                .Setup(u => u.CheckFileIsNotOwnedByCurrentGroup(s_customJsonPath))
                .Returns(true);
            t_environmentOperations
                .Setup(e => e.GetEnvironmentVariable(SFCredentialManagerFileImpl.CredentialCacheDirectoryEnvironmentName))
                .Returns(CustomJsonDir);
            t_fileOperations
                .SetupSequence(f => f.Exists(s_customJsonPath))
                .Returns(true);

            SFCredentialManagerFactory.SetCredentialManager(new SFCredentialManagerFileImpl(t_fileOperations.Object, t_directoryOperations.Object, t_unixOperations.Object, t_environmentOperations.Object));
            _credentialManager = SFCredentialManagerFactory.GetCredentialManager();

            // act
            var thrown = Assert.Throws<SecurityException>(() => _credentialManager.GetCredentials("key"));

            // assert
            Assert.That(thrown.Message, Does.Contain("Attempting to read a file not owned by the effective group of the current process"));
        }

        [Test]
        public void TestThatJsonFileIsCheckedIfItHasTooBroadPermissions()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("skip test on Windows");
            }

            // arrange
            t_unixOperations
                .Setup(u => u.CheckFileHasAnyOfPermissions(s_customJsonPath, FileAccessPermissions.GroupReadWriteExecute | FileAccessPermissions.OtherReadWriteExecute))
                .Returns(true);
            t_environmentOperations
                .Setup(e => e.GetEnvironmentVariable(SFCredentialManagerFileImpl.CredentialCacheDirectoryEnvironmentName))
                .Returns(CustomJsonDir);
            t_fileOperations
                .SetupSequence(f => f.Exists(s_customJsonPath))
                .Returns(true);

            SFCredentialManagerFactory.SetCredentialManager(new SFCredentialManagerFileImpl(t_fileOperations.Object, t_directoryOperations.Object, t_unixOperations.Object, t_environmentOperations.Object));
            _credentialManager = SFCredentialManagerFactory.GetCredentialManager();

            // act
            var thrown = Assert.Throws<SecurityException>(() => _credentialManager.GetCredentials("key"));

            // assert
            Assert.That(thrown.Message, Does.Contain("Attempting to read a file with too broad permissions assigned"));
        }
    }
}
