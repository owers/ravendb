﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Voron;
using Voron.Data.Compact;
using Voron.Data.Tables;
using Xunit;

namespace FastTests.Voron.Compact
{
    public class SimplePrefixTree : PrefixTreeStorageTests
    {
        private string Name = "MyTree";

        private void InitializeStorage()
        {            
            InitializeStorage(Name);
        }

        private void InitializeStorage(string treeName)
        {
            using (var tx = Env.WriteTransaction())
            {
                DocsSchema.Create(tx, "docs");
                tx.CreatePrefixTree(treeName);

                tx.Commit();
            }
        }


        [Fact]
        public void Construction()
        {
            InitializeStorage();

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);

                Assert.Equal(0, tree.Count);
                Assert.Equal(Slice.BeforeAllKeys, tree.FirstKey());
                Assert.Equal(Slice.AfterAllKeys, tree.LastKey());

                StructuralVerify(tree);
            }
        }

        [Fact]
        public void Operations_SingleElement_Invariants()
        {
            InitializeStorage();

            Slice key = new Slice(Encoding.UTF8.GetBytes("oren"));

            long recordId;
            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                recordId = SetHelper(docs, key, "eini");
                Assert.True(tree.Add(key, recordId));

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);

                var docs = new Table(DocsSchema, "docs", tx);
                docs.ReadByKey(key);

                Assert.Equal(key, tree.FirstKey());
                Assert.Equal(key, tree.LastKey());
                Assert.True(tree.Contains(key));

                long value;
                Assert.True(tree.TryGet(key, out value));
                Assert.Equal(recordId, value);

                StructuralVerify(tree);
            }
        }

        [Fact]
        public void Operations_SingleElement_Operations()
        {
            InitializeStorage();

            Slice key = new Slice(Encoding.UTF8.GetBytes("oren"));

            long recordId;
            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                recordId = SetHelper(docs, key, "eini");
                Assert.True(tree.Add(key, recordId));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);

                StructuralVerify(tree);

                // x+ = min{y ? S | y = x} (the successor of x in S) - Page 160 of [1]
                // Therefore the successor of the key "oren" is greater or equal to "oren"
                Assert.Equal(key, tree.Successor(key));
                Assert.Equal(Slice.AfterAllKeys, tree.Successor("qu"));

                // x- = max{y ? S | y < x} (the predecessor of x in S) - Page 160 of [1] 
                // Therefore the predecessor of the key "oren" is strictly less than "oren".
                Assert.Equal(Slice.BeforeAllKeys, tree.Predecessor(key));
                Assert.Equal(Slice.BeforeAllKeys, tree.Predecessor("aq"));
                Assert.Equal(key, tree.Predecessor("pq"));
            }
        }

        
        private long AddAndDumpToPrefixTree(PrefixTree tree, Table table, string key, string value)
        {
            long res = AddToPrefixTree(tree, table, key, value);
            DumpTree(tree);
            return res;
        }

        private long AddToPrefixTree(PrefixTree tree, Table table, string key, string value)
        {
            return AddToPrefixTree(tree, table, new Slice(Encoding.UTF8.GetBytes(key)), value);
        }

        private long AddToPrefixTree(PrefixTree tree, Table table, Slice key, string value)
        {
            long recordId = SetHelper(table, key, value);
            Assert.True(tree.Add(key, recordId));
            return recordId;
        }

        [Fact]
        public void Structure_MultipleBranchInsertion()
        {
            InitializeStorage();

            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "8Jp3", "8Jp3"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "GX37", "GX37"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "f04o", "f04o"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "KmGx", "KmGx"));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);
                StructuralVerify(tree);

                Assert.Equal(4, tree.Count);
            }
        }

        [Fact]
        public void Structure_MultipleBranchInsertion2()
        {
            InitializeStorage();

            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                Assert.NotEqual(-1, AddAndDumpToPrefixTree(tree, docs, "X2o", "X2o"));                
                Assert.NotEqual(-1, AddAndDumpToPrefixTree(tree, docs, "DWp", "DWp"));
                Assert.NotEqual(-1, AddAndDumpToPrefixTree(tree, docs, "C70", "C70"));
                Assert.NotEqual(-1, AddAndDumpToPrefixTree(tree, docs, "1b5", "1b5"));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);
                StructuralVerify(tree);

                Assert.Equal(4, tree.Count);
            }
        }

        [Fact]
        public void Structure_MultipleBranch_InternalExtent()
        {
            InitializeStorage();

            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "8Jp3V6sl", "8Jp3"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "VJ7hXe8d", "V6sl"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "39XCGX37", "GX37"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "f04oKmGx", "f04o"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "feiF1gdt", "KmGx"));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);
                StructuralVerify(tree);

                Assert.Equal(5, tree.Count);
            }
        }

        [Fact]
        public void Structure_MultipleBranch_InternalExtent2()
        {
            InitializeStorage();

            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "i", "8Jp3"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "4", "V6sl"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "j", "GX37"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "P", "f04o"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "8", "KmGx"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "3", "KmG3"));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);
                StructuralVerify(tree);

                Assert.Equal(6, tree.Count);
            }
        }

        [Fact]
        public void Structure_MultipleBranch_OrderPreservation()
        {
            InitializeStorage();

            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "8Jp3", "8Jp3"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "V6sl", "V6sl"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "GX37", "GX37"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "f04o", "f04o"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "KmGx", "KmGx"));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);
                StructuralVerify(tree);

                Assert.Equal(5, tree.Count);
            }
        }

        [Fact]
        public void Structure_MultipleBranch_OrderPreservation2()
        {
            InitializeStorage();

            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "1Z", "8Jp3"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "fG", "V6sl"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "dW", "GX37"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "8I", "f04o"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "7H", "KmGx"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "73", "KmGx"));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);
                StructuralVerify(tree);

                Assert.Equal(6, tree.Count);
            }
        }

        [Fact]
        public void Operations_Contains_SimpleCheck()
        {
            InitializeStorage();

            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "Sr1", "8Jp3"));
                Assert.True(tree.Contains(new Slice(Encoding.UTF8.GetBytes("Sr1"))));
                Assert.False(tree.Contains(new Slice(Encoding.UTF8.GetBytes("MiL"))));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);
                StructuralVerify(tree);

                Assert.Equal(1, tree.Count);
            }
        }

        [Fact]
        public void Structure_MultipleBranch_OrderPreservation3()
        {
            InitializeStorage();

            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "6b", "8Jp3"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "ab", "V6sl"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "dG", "GX37"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "3s", "f04o"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "8u", "KmGx"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "cI", "KmGx"));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);
                StructuralVerify(tree);

                Assert.Equal(6, tree.Count);
            }
        }

        [Fact]
        public void Structure_NodesTable_FailedTableVerify()
        {
            InitializeStorage();

            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "R", "1q"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "F", "3n"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "O", "6e"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "E", "Fs"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "Lr", "LD"));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "L5", "MU"));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);
                StructuralVerify(tree);

                Assert.Equal(6, tree.Count);
            }
        }

        [Fact]
        public void Addition_FailureToPass_QuickPath()
        {
            InitializeStorage();

            using (var tx = Env.WriteTransaction())
            {
                var docs = new Table(DocsSchema, "docs", tx);
                var tree = tx.CreatePrefixTree(Name);

                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "lJCn3J", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "4wLolJ", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "FZt4Dp", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "8NSagc", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "9eI05C", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "C4gnS4", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "PRjxjs", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "3M7Oxy", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "boKWpa", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "FLnjoZ", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "AE1Jlq", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "mbHypw", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "FLnjhT", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "fvrTYR", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "2pOGiH", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "RpmKwf", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "1ulQmV", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "rn8YRe", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "wfnTE2", string.Empty));
                Assert.NotEqual(-1, AddToPrefixTree(tree, docs, "rqqjR5", string.Empty));

                StructuralVerify(tree);

                tx.Commit();
            }

            using (var tx = Env.ReadTransaction())
            {
                var tree = tx.ReadPrefixTree(Name);
                StructuralVerify(tree);

                Assert.Equal(20, tree.Count);
            }
        }


        private static readonly string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        private static string GenerateRandomString(Random generator, int size)
        {
            var stringChars = new char[size];
            for (int i = 0; i < stringChars.Length; i++)
                stringChars[i] = chars[generator.Next(chars.Length)];

            return new string(stringChars);
        }


        public static IEnumerable<object[]> TreeSize
        {
            get
            {
                // Or this could read from a file. :)
                return new[]
                {
                    new object[] { 102, 4, 4 },
                    new object[] { 100, 4, 8 },
                    new object[] { 101, 2, 128 },
                    new object[] { 100, 8, 5 },
                    new object[] { 100, 16, 168 }
                };
            }
        }

        [Fact]
        public void Structure_RandomTester()
        {
            int count = 1000;
            int size = 5;
            for (int i = 0; i < 1; i++)
            {
                var keys = new Slice[count];

                InitializeStorage();

                var insertedKeys = new HashSet<Slice>();

                using (var tx = Env.WriteTransaction())
                {
                    var docs = new Table(DocsSchema, "docs", tx);
                    var tree = tx.CreatePrefixTree(Name);

                    var generator = new Random(i + size);
                    for (int j = 0; j < count; j++)
                    {
                        string key = GenerateRandomString(generator, size);
                        var keySlice = new Slice(Encoding.UTF8.GetBytes(key));

                        if (!insertedKeys.Contains(keySlice))
                        {
                            Assert.False(tree.Contains(keySlice));
                            Assert.NotEqual(-1, AddToPrefixTree(tree, docs, keySlice, key));
                        }

                        keys[j] = keySlice;
                        insertedKeys.Add(keySlice);

                        StructuralVerify(tree);
                    }

                    //  // The chance that we hit an already existing key is very low with a different seed so checking the inserted key is probably going to return false.
                    //  generator = new Random(i + size + 1);
                    //  for (int j = 0; j < count; j++)
                    //  {
                    //      string key = GenerateRandomString(generator, size);

                    //      if (!insertedKeys.Contains(key))
                    //          Assert.False(tree.Delete(key));
                    //  }

                    //  // We reply the insertion order to delete. 
                    //  generator = new Random(i + size);
                    //  for (int j = 0; j < count; j++)
                    //  {
                    //      string key = GenerateRandomString(generator, size);

                    //      bool removed = tree.Delete(key);
                    //      Assert.True(removed);
                    //  }

                    tx.Commit();
                }

                using (var tx = Env.ReadTransaction())
                {
                    var tree = tx.ReadPrefixTree(Name);
                    StructuralVerify(tree);

                    Assert.Equal(insertedKeys.Count, tree.Count);
                }
            }
        }

        public void Structure_GenerateTestCases()
        {            
            int count = 100;
            int size = 3;

            string[] smallestRepro = null;
            int smallest = int.MaxValue;

            try
            {
                using (var tx = Env.WriteTransaction())
                {
                    DocsSchema.Create(tx, "docs");
                    tx.Commit();
                }

                for (int i = 0; i < count; i++)
                {
                    var keys = new string[count];

                    var insertedKeys = new HashSet<string>();

                    using (var tx = Env.WriteTransaction())
                    {
                        var docs = new Table(DocsSchema, "docs", tx);
                        var tree = tx.CreatePrefixTree(Name + i);

                        var generator = new Random(i + size);

                        try
                        {
                            for (int j = 0; j < count; j++)
                            {
                                string key = GenerateRandomString(generator, size);
                                var keySlice = new Slice(Encoding.UTF8.GetBytes(key));

                                if (!insertedKeys.Contains(key))
                                {
                                    keys[j] = key;
                                    insertedKeys.Add(key);

                                    Assert.False(tree.Contains(keySlice));
                                    Assert.NotEqual(-1, AddToPrefixTree(tree, docs, keySlice, "8Jp3"));
                                }

                                StructuralVerify(tree);
                            }
                        }
                        catch
                        {
                            Console.WriteLine($"Found one of size {insertedKeys.Count}.");
                            if (smallest > insertedKeys.Count)
                            {
                                smallest = insertedKeys.Count;
                                smallestRepro = keys;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Something else happens.");
                Console.WriteLine(e);
                Console.WriteLine("Best found before failing:");
            }

            if (smallest != int.MaxValue)
            {
                var textWriter = Console.Out;
                for (int i = 0; i < smallest; i++)
                    textWriter.WriteLine(smallestRepro[i]);
            }
            else Console.WriteLine("No structural fail found.");

            Console.WriteLine("Done!");
            Console.ReadLine();

        }
    }
}
