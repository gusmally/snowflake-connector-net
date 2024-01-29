using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using NUnit.Framework;
using Snowflake.Data.Client;
using Snowflake.Data.Core.Session;
using Snowflake.Data.Tests.Util;

namespace Snowflake.Data.Tests.IntegrationTests
{
    [TestFixture]
    [NonParallelizable]
    public class ConnectionSinglePoolCacheIT: SFBaseTest
    {
        private readonly PoolConfig _previousPoolConfig = new PoolConfig();

        [SetUp]
        public new void BeforeTest()
        {
            SnowflakeDbConnectionPool.SetConnectionPoolVersion(ConnectionPoolType.SingleConnectionCache);
            SnowflakeDbConnectionPool.ClearAllPools();
            SnowflakeDbConnectionPool.SetPooling(true);
        }
        
        [TearDown]
        public new void AfterTest()
        {
            _previousPoolConfig.Reset();
        }
        
        [OneTimeTearDown]
        public static void AfterAllTests()
        {
            SnowflakeDbConnectionPool.ClearAllPools();
        }        
        
        [Test]
        public void TestConcurrentConnectionPooling()
        {
            // add test case name in connection string to make in unique for each test case
            string connStr = ConnectionString + ";application=TestConcurrentConnectionPooling";
            ConcurrentPoolingHelper(connStr, true);
        }

        [Test]
        // test connection pooling with concurrent connection and no close
        // call for connection. Connection is closed when Dispose() is called
        // by framework.
        public void TestConcurrentConnectionPoolingDispose()
        {
            // add test case name in connection string to make in unique for each test case
            string connStr = ConnectionString + ";application=TestConcurrentConnectionPoolingNoClose";
            ConcurrentPoolingHelper(connStr, false);
        }

        static void ConcurrentPoolingHelper(string connectionString, bool closeConnection)
        {
            // thread number a bit larger than pool size so some connections
            // would fail on pooling while some connections could success
            const int ThreadNum = 12;
            // set short pooling timeout to cover the case that connection expired
            const int PoolTimeout = 3;

            // reset to default settings in case it changed by other test cases
            Assert.AreEqual(true, SnowflakeDbConnectionPool.GetPool(connectionString).GetPooling()); // to instantiate pool
            SnowflakeDbConnectionPool.SetMaxPoolSize(10);
            SnowflakeDbConnectionPool.SetTimeout(PoolTimeout);

            var threads = new Task[ThreadNum];
            for (int i = 0; i < ThreadNum; i++)
            {
                threads[i] = Task.Factory.StartNew(() =>
                {
                    QueryExecutionThread(connectionString, closeConnection);
                });
            }
            Task.WaitAll(threads);
        }
        
        // thead to execute query with new connection in a loop
        static void QueryExecutionThread(string connectionString, bool closeConnection)
        {
            for (int i = 0; i < 100; i++)
            {
                using (DbConnection conn = new SnowflakeDbConnection(connectionString))
                {
                    conn.Open();
                    using (DbCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "select 1, 2, 3";
                        try
                        {
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    for (int j = 0; j < reader.FieldCount; j++)
                                    {
                                        // Process each column as appropriate
                                        reader.GetFieldValue<object>(j);
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Assert.Fail("Caught unexpected exception: " + e);
                        }
                    }

                    if (closeConnection)
                    {
                        conn.Close();
                    }
                }
            }
        }

        [Test]
        public void TestPoolContainsClosedConnections() // old name: TestConnectionPool
        {
            var conn1 = new SnowflakeDbConnection(ConnectionString);
            conn1.Open();
            Assert.AreEqual(ConnectionState.Open, conn1.State);
            conn1.Close();
            Assert.AreEqual(1, SnowflakeDbConnectionPool.GetPool(ConnectionString).GetCurrentPoolSize());

            var conn2 = new SnowflakeDbConnection();
            conn2.ConnectionString = ConnectionString;
            conn2.Open();
            Assert.AreEqual(ConnectionState.Open, conn2.State);
            Assert.AreEqual(0, SnowflakeDbConnectionPool.GetPool(ConnectionString).GetCurrentPoolSize());

            conn2.Close();
            Assert.AreEqual(1, SnowflakeDbConnectionPool.GetPool(ConnectionString).GetCurrentPoolSize());
            Assert.AreEqual(ConnectionState.Closed, conn1.State);
            Assert.AreEqual(ConnectionState.Closed, conn2.State);
        }
        
        [Test]
        public void TestPoolContainsAtMostMaxPoolSizeConnections() // old name: TestConnectionPoolFull
        {
            SnowflakeDbConnectionPool.SetMaxPoolSize(2);

            var conn1 = new SnowflakeDbConnection();
            conn1.ConnectionString = ConnectionString;
            conn1.Open();
            Assert.AreEqual(ConnectionState.Open, conn1.State);

            var conn2 = new SnowflakeDbConnection();
            conn2.ConnectionString = ConnectionString + " retryCount=1";
            conn2.Open();
            Assert.AreEqual(ConnectionState.Open, conn2.State);
            Assert.AreEqual(0, SnowflakeDbConnectionPool.GetCurrentPoolSize());
            conn1.Close();
            conn2.Close();
            Assert.AreEqual(2, SnowflakeDbConnectionPool.GetCurrentPoolSize());
            var conn3 = new SnowflakeDbConnection();
            conn3.ConnectionString = ConnectionString + "  retryCount=2";
            conn3.Open();
            Assert.AreEqual(ConnectionState.Open, conn3.State);

            var conn4 = new SnowflakeDbConnection();
            conn4.ConnectionString = ConnectionString + "  retryCount=3";
            conn4.Open();
            Assert.AreEqual(ConnectionState.Open, conn4.State);

            conn3.Close();
            Assert.AreEqual(2, SnowflakeDbConnectionPool.GetCurrentPoolSize());
            conn4.Close();
            Assert.AreEqual(2, SnowflakeDbConnectionPool.GetCurrentPoolSize());

            Assert.AreEqual(ConnectionState.Closed, conn1.State);
            Assert.AreEqual(ConnectionState.Closed, conn2.State);
            Assert.AreEqual(ConnectionState.Closed, conn3.State);
            Assert.AreEqual(ConnectionState.Closed, conn4.State);
            SnowflakeDbConnectionPool.ClearAllPools();
        }

        [Test]
        public void TestConnectionPoolDisableFromPoolManagerLevel()
        {
            // arrange
            SnowflakeDbConnectionPool.SetPooling(false);
            var conn1 = new SnowflakeDbConnection();
            conn1.ConnectionString = ConnectionString;
            
            // act
            conn1.Open();
            
            // assert
            Assert.AreEqual(ConnectionState.Open, conn1.State);
            Assert.AreEqual(0, SnowflakeDbConnectionPool.GetCurrentPoolSize());
            
            // act
            conn1.Close();

            // assert
            Assert.AreEqual(ConnectionState.Closed, conn1.State);
            Assert.AreEqual(0, SnowflakeDbConnectionPool.GetCurrentPoolSize());
        }
        
        [Test]
        public void TestConnectionPoolDisable()
        {
            // arrange
            var pool = SnowflakeDbConnectionPool.GetPool(ConnectionString);
            pool.SetPooling(false);
            var conn1 = new SnowflakeDbConnection();
            conn1.ConnectionString = ConnectionString;
            
            // act
            conn1.Open();
            
            // assert
            Assert.AreEqual(ConnectionState.Open, conn1.State);
            Assert.AreEqual(0, pool.GetCurrentPoolSize());
            
            // act
            conn1.Close();

            // assert
            Assert.AreEqual(ConnectionState.Closed, conn1.State);
            Assert.AreEqual(0, pool.GetCurrentPoolSize());
        }

        [Test]
        public void TestConnectionPoolClean()
        {
            SnowflakeDbConnectionPool.SetMaxPoolSize(2);
            var conn1 = new SnowflakeDbConnection();
            conn1.ConnectionString = ConnectionString;
            conn1.Open();
            Assert.AreEqual(ConnectionState.Open, conn1.State);

            var conn2 = new SnowflakeDbConnection();
            conn2.ConnectionString = ConnectionString + " retryCount=1";
            conn2.Open();
            Assert.AreEqual(ConnectionState.Open, conn2.State);

            var conn3 = new SnowflakeDbConnection();
            conn3.ConnectionString = ConnectionString + "  retryCount=2";
            conn3.Open();
            Assert.AreEqual(ConnectionState.Open, conn3.State);

            conn1.Close();
            conn2.Close();
            Assert.AreEqual(2, SnowflakeDbConnectionPool.GetCurrentPoolSize());
            SnowflakeDbConnectionPool.ClearAllPools();
            Assert.AreEqual(0, SnowflakeDbConnectionPool.GetCurrentPoolSize());
            conn3.Close();
            Assert.AreEqual(1, SnowflakeDbConnectionPool.GetCurrentPoolSize());

            Assert.AreEqual(ConnectionState.Closed, conn1.State);
            Assert.AreEqual(ConnectionState.Closed, conn2.State);
            Assert.AreEqual(ConnectionState.Closed, conn3.State);
        }
    }
}
